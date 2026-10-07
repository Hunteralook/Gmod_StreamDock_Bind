using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;

namespace NetSwitchPlugin
{
    // zapret (winws.exe + WinDivert) needs administrator rights. The button writes small
    // start/stop scripts and runs them either through UAC or through scheduled tasks
    // that were created once with the highest privileges.
    internal static class Zapret
    {
        public const string StartTaskName = @"\StreamDock NetSwitch\Zapret Start";
        public const string StopTaskName = @"\StreamDock NetSwitch\Zapret Stop";

        private const string ProcessName = "winws";
        private static readonly Regex ServiceNamePattern = new Regex(@"^[A-Za-z0-9_.\-]+$");

        public static string ScriptPath(string kind)
        {
            return Path.Combine(Program.PluginDirectory, "zapret-" + kind + ".cmd");
        }

        public static LinkState GetState(Dictionary<string, object> settings)
        {
            if (Shell.ProcessRunning(ProcessName)) return LinkState.On;

            if (Json.GetString(settings, "zapretRun", "bat") == "service")
            {
                ServiceControllerStatus? status = ServiceStatus(ServiceName(settings));
                if (status == null) return LinkState.NotFound;
                if (status == ServiceControllerStatus.StartPending || status == ServiceControllerStatus.StopPending)
                {
                    return LinkState.Busy;
                }
                return LinkState.Off;
            }

            return Json.GetTrimmed(settings, "zapretDir").Length == 0 ? LinkState.NotConfigured : LinkState.Off;
        }

        public static bool Press(Dictionary<string, object> settings)
        {
            string mode = Json.GetString(settings, "mode", "toggle");
            bool running = Shell.ProcessRunning(ProcessName);
            bool start = mode == "on" || (mode != "off" && !running);
            if (start && running) return true;

            string error;
            if (!WriteScripts(settings, out error))
            {
                Log.Write("zapret: " + error);
                return false;
            }

            string kind = start ? "start" : "stop";
            Log.Write("zapret: " + kind + " requested.");

            if (Json.GetString(settings, "elevation", "uac") == "task")
            {
                CommandResult result = Shell.Run(
                    "schtasks.exe",
                    "/run /tn \"" + (start ? StartTaskName : StopTaskName) + "\"",
                    15000);
                if (result.ExitCode != 0)
                {
                    Log.Write("zapret: scheduled task did not run. Create the tasks in the button settings. " + result.Output.Trim());
                }
                return result.ExitCode == 0;
            }

            return Shell.RunElevated(
                "cmd.exe",
                Shell.CmdArguments(ScriptPath(kind)),
                start ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Hidden,
                0);
        }

        private static bool WriteScripts(Dictionary<string, object> settings, out string error)
        {
            string startBody;
            string stopBody;

            if (Json.GetString(settings, "zapretRun", "bat") == "service")
            {
                string service = ServiceName(settings);
                if (!ServiceNamePattern.IsMatch(service))
                {
                    error = "invalid service name.";
                    return false;
                }
                startBody = "net start \"" + service + "\"\r\n";
                stopBody = "net stop \"" + service + "\"\r\ntaskkill /IM winws.exe /F >nul 2>&1\r\n";
            }
            else
            {
                string directory = Json.GetTrimmed(settings, "zapretDir");
                string strategy = Json.GetTrimmed(settings, "zapretBat");
                if (directory.Length == 0 || !Directory.Exists(directory))
                {
                    error = "zapret folder is not set or does not exist.";
                    return false;
                }
                if (!IsPlainBatchName(strategy) || !File.Exists(Path.Combine(directory, strategy)))
                {
                    error = "strategy file is not set or does not exist.";
                    return false;
                }
                startBody =
                    "cd /d \"" + Shell.EscapeForBatch(directory) + "\"\r\n" +
                    "call \"" + Shell.EscapeForBatch(strategy) + "\"\r\n";
                stopBody = "taskkill /IM winws.exe /F >nul 2>&1\r\n";
            }

            try
            {
                Shell.WriteBatch(ScriptPath("start"), startBody);
                Shell.WriteBatch(ScriptPath("stop"), stopBody);
            }
            catch (Exception ex)
            {
                error = "cannot write scripts: " + ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        private static string ServiceName(Dictionary<string, object> settings)
        {
            string name = Json.GetTrimmed(settings, "serviceName");
            return name.Length == 0 ? "zapret" : name;
        }

        public static bool IsPlainBatchName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '\\', '/', ':', '"' }) >= 0 || name.Contains("..")) return false;
            string extension = Path.GetExtension(name).ToLowerInvariant();
            return extension == ".bat" || extension == ".cmd";
        }

        public static string[] ListStrategies(string directory)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return result.ToArray();

            try
            {
                foreach (string path in Directory.GetFiles(directory, "*.bat"))
                {
                    string name = Path.GetFileName(path);
                    // service.bat and service_*.bat manage the zapret service, they are not strategies.
                    if (name.StartsWith("service", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(name);
                }
            }
            catch
            {
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.ToArray();
        }

        public static bool WinwsPresent(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return false;
            return File.Exists(Path.Combine(directory, @"bin\winws.exe")) || File.Exists(Path.Combine(directory, "winws.exe"));
        }

        public static ServiceControllerStatus? ServiceStatus(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return null;
            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    return controller.Status;
                }
            }
            catch (Exception)
            {
                // A missing service throws InvalidOperationException; access problems mean the same for the button.
                return null;
            }
        }

        public static bool TasksInstalled()
        {
            return Shell.Run("schtasks.exe", "/query /tn \"" + StartTaskName + "\"", 10000).ExitCode == 0 &&
                Shell.Run("schtasks.exe", "/query /tn \"" + StopTaskName + "\"", 10000).ExitCode == 0;
        }

        public static bool InstallTasks()
        {
            string temp = Path.Combine(Path.GetTempPath(), "netswitch-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                foreach (string kind in new[] { "start", "stop" })
                {
                    if (!File.Exists(ScriptPath(kind)))
                    {
                        Shell.WriteBatch(ScriptPath(kind), "echo Configure the zapret button in StreamDock first.\r\npause\r\n");
                    }
                }

                string user = WindowsIdentity.GetCurrent().Name;
                string startXml = Path.Combine(temp, "start.xml");
                string stopXml = Path.Combine(temp, "stop.xml");
                File.WriteAllText(startXml, BuildTaskXml(user, ScriptPath("start")), Encoding.Unicode);
                File.WriteAllText(stopXml, BuildTaskXml(user, ScriptPath("stop")), Encoding.Unicode);

                string installer = Path.Combine(temp, "install.cmd");
                Shell.WriteBatch(installer,
                    "schtasks /create /f /tn \"" + StartTaskName + "\" /xml \"" + Shell.EscapeForBatch(startXml) + "\" || exit /b 1\r\n" +
                    "schtasks /create /f /tn \"" + StopTaskName + "\" /xml \"" + Shell.EscapeForBatch(stopXml) + "\" || exit /b 1\r\n");

                bool ok = Shell.RunElevated("cmd.exe", Shell.CmdArguments(installer), ProcessWindowStyle.Hidden, 30000);
                Log.Write("zapret: scheduled tasks " + (ok ? "created." : "were not created."));
                return ok && TasksInstalled();
            }
            catch (Exception ex)
            {
                Log.Write("zapret: cannot create scheduled tasks: " + ex.Message);
                return false;
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        public static bool RemoveTasks()
        {
            bool ok = Shell.RunElevated(
                "cmd.exe",
                "/c schtasks /delete /f /tn \"" + StartTaskName + "\" & schtasks /delete /f /tn \"" + StopTaskName + "\"",
                ProcessWindowStyle.Hidden,
                30000);
            Log.Write("zapret: scheduled tasks removal " + (ok ? "finished." : "failed."));
            return !TasksInstalled();
        }

        public static string BuildTaskXml(string user, string scriptPath)
        {
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Started by the StreamDock NetSwitch plugin.</Description></RegistrationInfo>\r\n" +
                "  <Principals>\r\n" +
                "    <Principal id=\"Author\">\r\n" +
                "      <UserId>" + SecurityElement.Escape(user) + "</UserId>\r\n" +
                "      <LogonType>InteractiveToken</LogonType>\r\n" +
                "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
                "    </Principal>\r\n" +
                "  </Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\">\r\n" +
                "    <Exec>\r\n" +
                "      <Command>cmd.exe</Command>\r\n" +
                "      <Arguments>" + SecurityElement.Escape(Shell.CmdArguments(scriptPath)) + "</Arguments>\r\n" +
                "    </Exec>\r\n" +
                "  </Actions>\r\n" +
                "</Task>\r\n";
        }
    }
}
