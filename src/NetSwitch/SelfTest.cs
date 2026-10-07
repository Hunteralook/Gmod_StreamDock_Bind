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

            ServerEntry vless = Subscription.ParseLink("vless://11111111-2222-3333-4444-555555555555@nl.example.com:443?type=tcp&security=reality#%F0%9F%87%B3%F0%9F%87%B1%20Netherlands");
            Check(failures, "vless host", vless != null && vless.Host == "nl.example.com" && vless.Port == 443 && !vless.Udp);
            Check(failures, "vless name", vless != null && vless.Name == "\U0001F1F3\U0001F1F1 Netherlands");
            Check(failures, "short name", ServerButton.ShortName("\U0001F1F3\U0001F1F1 Netherlands #1", "x") == "NL Netherl");
            ServerEntry vmess = Subscription.ParseLink("vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"ps\":\"DE\",\"add\":\"1.2.3.4\",\"port\":\"8443\"}")));
            Check(failures, "vmess", vmess != null && vmess.Host == "1.2.3.4" && vmess.Port == 8443 && vmess.Name == "DE");
            ServerEntry ss = Subscription.ParseLink("ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:pw")) + "@[2001:db8::1]:8388#SS");
            Check(failures, "shadowsocks ipv6", ss != null && ss.Host == "2001:db8::1" && ss.Port == 8388);
            ServerEntry hy2 = Subscription.ParseLink("hy2://secret@hy.example.com:5443?sni=x#HY");
            Check(failures, "hysteria udp", hy2 != null && hy2.Udp && hy2.Port == 5443);

            string body = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "#profile-title: base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Мой VPN")) + "\n" +
                "trojan://pw@a.example.com:443#A\nvless://id@b.example.com:8443#B\n"));
            SubscriptionInfo info = Subscription.Parse(body, "upload=1073741824; download=1073741824; total=10737418240; expire=0", null);
            Check(failures, "subscription servers", info.Servers.Count == 2 && info.Error == null);
            Check(failures, "subscription title", info.Title == "Мой VPN");
            Check(failures, "subscription traffic", SubscriptionButton.TrafficLeft(info) == "8 ГБ" && SubscriptionButton.DaysLeft(info) == "∞");
            SubscriptionInfo json = Subscription.Parse(
                "[{\"remarks\":\"JSON\",\"outbounds\":[{\"protocol\":\"vless\",\"settings\":{\"vnext\":[{\"address\":\"c.example.com\",\"port\":443}]}}]}]",
                null, null);
            Check(failures, "json subscription", json.Servers.Count == 1 && json.Servers[0].Name == "JSON" && json.Servers[0].Port == 443);
            Check(failures, "crypt subscription", Subscription.Parse("happ://crypt3/abc", null, null).Error != null);

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
