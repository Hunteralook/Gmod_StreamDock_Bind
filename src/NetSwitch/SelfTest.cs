using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace NetSwitchPlugin
{
    // NetSwitch.exe --self-test checks the parsing helpers without touching the system
    // and writes the result to self-test.json next to the executable.
    internal static class SelfTest
    {
        public static int Run()
        {
            List<string> failures = new List<string>();

            Check(failures, "warp connected", Warp.ParseStatus("Status update: Connected\r\nNetwork: healthy") == LinkState.On);
            Check(failures, "warp disconnected", Warp.ParseStatus("Status update: Disconnected\r\nReason: Manual Disconnection") == LinkState.Off);
            Check(failures, "warp connecting", Warp.ParseStatus("Status update: Connecting\r\nReason: Checking connectivity") == LinkState.Busy);
            Check(failures, "warp daemon down", Warp.ParseStatus("Unable to connect to the CloudflareWARP daemon") == LinkState.Error);
            Check(failures, "warp old cli", Warp.RejectsAcceptTos("error: Found argument '--accept-tos' which wasn't expected"));
            Check(failures, "warp normal error", !Warp.RejectsAcceptTos("Error: registration missing"));

            Check(failures, "happ quoted", Happ.ExtractExecutable("\"C:\\Program Files\\Happ\\Happ.exe\" \"%1\"") == "C:\\Program Files\\Happ\\Happ.exe");
            Check(failures, "happ plain", Happ.ExtractExecutable("C:\\Happ\\Happ.exe %1") == "C:\\Happ\\Happ.exe");
            Check(failures, "happ empty", Happ.ExtractExecutable("") == null);

            Check(failures, "bat name", Zapret.IsPlainBatchName("general (ALT2).bat"));
            Check(failures, "bat traversal", !Zapret.IsPlainBatchName("..\\evil.bat"));
            Check(failures, "bat extension", !Zapret.IsPlainBatchName("winws.exe"));
            Check(failures, "batch percent", Shell.EscapeForBatch("C:\\50%\\zapret") == "C:\\50%%\\zapret");
            Check(failures, "cmd arguments", Shell.CmdArguments("C:\\a b\\x.cmd") == "/c \"\"C:\\a b\\x.cmd\"\"");

            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(Zapret.BuildTaskXml("PC\\User & Co", "C:\\Plugins\\zapret-start.cmd"));
                XmlNamespaceManager names = new XmlNamespaceManager(document.NameTable);
                names.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                Check(failures, "task user", document.SelectSingleNode("//t:UserId", names).InnerText == "PC\\User & Co");
                Check(failures, "task arguments", document.SelectSingleNode("//t:Arguments", names).InnerText == "/c \"\"C:\\Plugins\\zapret-start.cmd\"\"");
            }
            catch (Exception ex)
            {
                failures.Add("task xml: " + ex.Message);
            }

            string report = failures.Count == 0
                ? "{\"passed\":true}"
                : "{\"passed\":false,\"failures\":[\"" + string.Join("\",\"", failures.ToArray()).Replace("\\", "\\\\") + "\"]}";
            Console.WriteLine(report);
            try
            {
                File.WriteAllText(Path.Combine(Program.PluginDirectory, "self-test.json"), report, new UTF8Encoding(false));
            }
            catch
            {
            }
            return failures.Count == 0 ? 0 : 4;
        }

        private static void Check(List<string> failures, string name, bool passed)
        {
            if (!passed) failures.Add(name);
        }
    }
}
