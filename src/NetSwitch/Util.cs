using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NetSwitchPlugin
{
    internal enum LinkState
    {
        Unknown,
        Off,
        On,
        Busy,
        NotFound,
        NotConfigured,
        Error
    }

    internal sealed class CommandResult
    {
        public bool Started;
        public bool TimedOut;
        public int ExitCode = -1;
        public string Output = string.Empty;
    }

    internal static class Shell
    {
        public static CommandResult Run(string fileName, string arguments, int timeoutMs)
        {
            CommandResult result = new CommandResult();
            ProcessStartInfo info = new ProcessStartInfo(fileName, arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            StringBuilder output = new StringBuilder();
            object gate = new object();
            DataReceivedEventHandler collect = delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (gate) output.AppendLine(e.Data);
            };

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                try
                {
                    if (!process.Start()) return result;
                }
                catch (Exception ex)
                {
                    Log.Write("Cannot start " + fileName + ": " + ex.Message);
                    return result;
                }

                result.Started = true;
                try { process.StandardInput.Close(); } catch { }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    result.TimedOut = true;
                    try { process.Kill(); } catch { }
                }
                else
                {
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }
            }

            lock (gate) result.Output = output.ToString();
            return result;
        }

        // Starts a process through ShellExecute with the "runas" verb, which shows the UAC prompt.
        // When waitMs is positive, waits for the process and reports its exit code.
        public static bool RunElevated(string fileName, string arguments, ProcessWindowStyle style, int waitMs)
        {
            ProcessStartInfo info = new ProcessStartInfo(fileName, arguments);
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WindowStyle = style;
            try
            {
                using (Process process = Process.Start(info))
                {
                    if (process == null || waitMs <= 0) return process != null;
                    if (!process.WaitForExit(waitMs)) return false;
                    return process.ExitCode == 0;
                }
            }
            catch (Win32Exception ex)
            {
                Log.Write(ex.NativeErrorCode == 1223
                    ? "UAC request was cancelled by the user."
                    : "Elevated start of " + fileName + " failed: " + ex.Message);
                return false;
            }
        }

        public static bool Open(string target, string workingDirectory, string logName)
        {
            ProcessStartInfo info = new ProcessStartInfo(target);
            info.UseShellExecute = true;
            if (!string.IsNullOrEmpty(workingDirectory)) info.WorkingDirectory = workingDirectory;
            try
            {
                using (Process.Start(info)) { }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Cannot open " + logName + ": " + ex.Message);
                return false;
            }
        }

        // Runs a generated .cmd script either elevated through UAC or plainly.
        public static string CmdArguments(string scriptPath)
        {
            return "/c \"\"" + scriptPath + "\"\"";
        }

        // Batch files expand %VAR%; a literal percent sign must be doubled.
        public static string EscapeForBatch(string value)
        {
            return (value ?? string.Empty).Replace("%", "%%");
        }

        // Batch files are read in the console OEM code page, so Cyrillic paths are written in it.
        public static void WriteBatch(string path, string body)
        {
            Encoding encoding;
            try
            {
                encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch
            {
                encoding = Encoding.ASCII;
            }
            File.WriteAllText(path, "@echo off\r\n" + body, encoding);
        }

        public static bool ProcessRunning(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            bool running = processes.Length > 0;
            foreach (Process process in processes) process.Dispose();
            return running;
        }

        public static string ProgramFiles()
        {
            string native = Environment.GetEnvironmentVariable("ProgramW6432");
            return string.IsNullOrEmpty(native)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                : native;
        }
    }

    internal static class SystemProxy
    {
        private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const int InternetOptionSettingsChanged = 39;
        private const int InternetOptionRefresh = 37;

        // Turns off the Windows system proxy only when it points to this computer,
        // which is what proxy clients leave behind when they are killed.
        public static bool DisableIfLocal()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey, true))
            {
                if (key == null) return false;
                object enabled = key.GetValue("ProxyEnable");
                if (!(enabled is int) || (int)enabled == 0) return false;

                string server = Convert.ToString(key.GetValue("ProxyServer") ?? string.Empty).ToLowerInvariant();
                if (!server.Contains("127.0.0.1") && !server.Contains("localhost") && !server.Contains("[::1]"))
                {
                    return false;
                }

                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }

            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
            return true;
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int bufferLength);
    }

    internal static class Json
    {
        public static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                Dictionary<string, object> result = value as Dictionary<string, object>;
                if (result != null) return result;
            }
            return new Dictionary<string, object>();
        }

        public static string GetString(Dictionary<string, object> source, string key, string fallback)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return fallback;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static string GetTrimmed(Dictionary<string, object> source, string key)
        {
            return GetString(source, key, string.Empty).Trim().Trim('"').Trim();
        }

        public static bool GetBool(Dictionary<string, object> source, string key, bool fallback)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return fallback;
            if (value is bool) return (bool)value;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            bool parsed;
            if (bool.TryParse(text, out parsed)) return parsed;
            int number;
            if (int.TryParse(text, out number)) return number != 0;
            return fallback;
        }

        public static string[] GetStrings(Dictionary<string, object> source, string key)
        {
            object value;
            List<string> result = new List<string>();
            if (source != null && source.TryGetValue(key, out value))
            {
                IEnumerable items = value as IEnumerable;
                if (items != null && !(value is string))
                {
                    foreach (object item in items)
                    {
                        if (item != null) result.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
                    }
                }
            }
            return result.ToArray();
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();
        public static string Path;

        public static void Write(string message)
        {
            if (string.IsNullOrEmpty(Path)) return;
            try
            {
                lock (Gate)
                {
                    FileInfo file = new FileInfo(Path);
                    if (file.Exists && file.Length > 1048576)
                    {
                        string old = Path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Path, old);
                    }
                    File.AppendAllText(
                        Path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }
}
