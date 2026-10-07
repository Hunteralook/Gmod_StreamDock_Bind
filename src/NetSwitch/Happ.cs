using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace NetSwitchPlugin
{
    // Happ has no documented command line for connecting, so the button starts and closes
    // the application itself and can open happ:// links handled by it.
    internal static class Happ
    {
        public static string ResolveExe(Dictionary<string, object> settings)
        {
            string configured = Json.GetTrimmed(settings, "happPath");
            if (configured.Length > 0) return File.Exists(configured) ? configured : null;

            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (RegistryKey key = root.OpenSubKey(@"Software\Classes\happ\shell\open\command"))
                    {
                        if (key == null) continue;
                        string exe = ExtractExecutable(Convert.ToString(key.GetValue(string.Empty)));
                        if (exe != null && File.Exists(exe)) return exe;
                    }
                }
                catch
                {
                }
            }

            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Happ\Happ.exe"),
                Path.Combine(Shell.ProgramFiles(), @"Happ\Happ.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Happ\Happ.exe")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // Takes the executable from a shell command such as "C:\Happ\Happ.exe" "%1".
        public static string ExtractExecutable(string command)
        {
            string text = (command ?? string.Empty).Trim();
            if (text.Length == 0) return null;

            if (text[0] == '"')
            {
                int end = text.IndexOf('"', 1);
                return end > 1 ? text.Substring(1, end - 1) : null;
            }

            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? text.Substring(0, exe + 4) : null;
        }

        public static LinkState GetState(Dictionary<string, object> settings)
        {
            string exe = ResolveExe(settings);
            if (exe == null) return LinkState.NotFound;
            return Shell.ProcessRunning(Path.GetFileNameWithoutExtension(exe)) ? LinkState.On : LinkState.Off;
        }

        public static bool Press(Dictionary<string, object> settings)
        {
            string mode = Json.GetString(settings, "mode", "toggle");
            if (mode == "link") return OpenLink(Json.GetTrimmed(settings, "happLink"));

            string exe = ResolveExe(settings);
            if (exe == null)
            {
                Log.Write("Happ: Happ.exe not found.");
                return false;
            }

            string processName = Path.GetFileNameWithoutExtension(exe);
            bool running = Shell.ProcessRunning(processName);
            bool start = mode == "on" || (mode != "off" && !running);

            if (start)
            {
                if (running) return true;
                Log.Write("Happ: starting application.");
                return Shell.Open(exe, Path.GetDirectoryName(exe), exe);
            }

            return Stop(processName, Json.GetBool(settings, "resetProxy", true));
        }

        private static bool OpenLink(string link)
        {
            if (!link.StartsWith("happ://", StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("Happ: the link must start with happ://.");
                return false;
            }
            Log.Write("Happ: opening link.");
            // The link itself may carry a subscription token, so it is not logged.
            return Shell.Open(link, null, "happ:// link");
        }

        private static bool Stop(string processName, bool resetProxy)
        {
            Log.Write("Happ: closing application.");
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try { process.CloseMainWindow(); } catch { }
                }
            }

            // Apps that minimize to the tray ignore the close request; they are terminated after a pause.
            for (int i = 0; i < 15 && Shell.ProcessRunning(processName); i++) Thread.Sleep(200);

            bool accessDenied = false;
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch (Win32Exception)
                    {
                        accessDenied = true;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }

            if (accessDenied)
            {
                // Happ started as administrator (TUN mode) can only be closed from an elevated process.
                Shell.RunElevated("taskkill.exe", "/IM \"" + processName + ".exe\" /T /F", ProcessWindowStyle.Hidden, 15000);
            }

            bool stopped = !Shell.ProcessRunning(processName);
            if (stopped && resetProxy && SystemProxy.DisableIfLocal())
            {
                Log.Write("Happ: local system proxy left after closing was turned off.");
            }
            return stopped;
        }
    }
}
