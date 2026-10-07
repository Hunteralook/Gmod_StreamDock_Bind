using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace NetSwitchPlugin
{
    internal sealed class Display
    {
        public int State;
        public string Title;

        public Display(int state, string title)
        {
            State = state;
            Title = title;
        }
    }

    // A button bound to one server from the subscription: shows its name and latency.
    internal static class ServerButton
    {
        private const int TimeoutMs = 3000;

        public static Display Refresh(Dictionary<string, object> settings)
        {
            string host = Json.GetTrimmed(settings, "serverHost");
            int port;
            if (host.Length == 0 || !int.TryParse(Json.GetString(settings, "serverPort", string.Empty), out port))
            {
                return new Display(0, "НАСТР.");
            }

            int latency = Latency.Measure(host, port, Json.GetBool(settings, "serverUdp", false), TimeoutMs);
            string label = Json.GetTrimmed(settings, "serverLabel");
            if (label.Length == 0) label = ShortName(Json.GetTrimmed(settings, "serverName"), host);

            return new Display(latency >= 0 ? 1 : 0, label + "\n" + (latency >= 0 ? latency + " мс" : "нет связи"));
        }

        // Turns "🇳🇱 Netherlands #1" into "NL Netherl" so it fits on a key.
        public static string ShortName(string name, string fallback)
        {
            string text = FlagsToLetters(string.IsNullOrEmpty(name) ? fallback : name);
            StringBuilder clean = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '.' || c == '#') clean.Append(c);
            }

            string result = clean.ToString().Trim();
            while (result.Contains("  ")) result = result.Replace("  ", " ");
            if (result.Length == 0) result = fallback;
            return result.Length > 10 ? result.Substring(0, 10).TrimEnd() : result;
        }

        private static string FlagsToLetters(string text)
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
                {
                    int code = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                    // Regional indicator symbols A..Z make up flag emoji.
                    if (code >= 0x1F1E6 && code <= 0x1F1FF) result.Append((char)('A' + code - 0x1F1E6));
                    else result.Append(' ');
                    continue;
                }
                result.Append(text[i]);
            }
            return result.ToString();
        }
    }

    internal static class SubscriptionButton
    {
        public static Display Refresh(Dictionary<string, object> settings, bool force)
        {
            string url = Json.GetTrimmed(settings, "subscriptionUrl");
            if (url.Length == 0) return new Display(0, "НАСТР.");

            SubscriptionInfo info = Subscription.Get(url, force);
            if (info.Error != null && info.Servers.Count == 0 && info.Expire == 0 && info.Total == 0)
            {
                return new Display(0, "ОШИБКА");
            }

            bool expired = info.Expire > 0 && info.Expire < UnixNow();
            return new Display(expired || info.Error != null ? 0 : 1, DaysLeft(info) + "\n" + TrafficLeft(info));
        }

        public static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static string DaysLeft(SubscriptionInfo info)
        {
            if (info.Expire <= 0) return "∞";
            long seconds = info.Expire - UnixNow();
            if (seconds <= 0) return "истекла";
            return (long)Math.Ceiling(seconds / 86400.0) + " дн.";
        }

        public static string TrafficLeft(SubscriptionInfo info)
        {
            if (info.Total <= 0) return "безлимит";
            return FormatBytes(Math.Max(0, info.Total - info.Upload - info.Download));
        }

        public static string FormatBytes(long bytes)
        {
            CultureInfo russian = CultureInfo.GetCultureInfo("ru-RU");
            double gigabytes = bytes / 1073741824.0;
            if (gigabytes >= 100) return gigabytes.ToString("0", russian) + " ГБ";
            if (gigabytes >= 1) return gigabytes.ToString("0.#", russian) + " ГБ";
            return (bytes / 1048576.0).ToString("0", russian) + " МБ";
        }
    }

    internal static class Latency
    {
        // TCP: time to complete the TCP handshake with the server, like Happ's "TCP ping".
        // UDP protocols (Hysteria, TUIC, WireGuard) do not accept TCP, so ICMP is used for them.
        // Returns milliseconds, or -1 when the server did not answer.
        public static int Measure(string host, int port, bool udp, int timeoutMs)
        {
            if (udp) return Icmp(host, timeoutMs);

            IPAddress address = Resolve(host);
            if (address == null) return -1;

            using (Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    IAsyncResult pending = socket.BeginConnect(address, port, null, null);
                    if (!pending.AsyncWaitHandle.WaitOne(timeoutMs)) return -1;
                    socket.EndConnect(pending);
                    return (int)Math.Max(1, watch.ElapsedMilliseconds);
                }
                catch (SocketException)
                {
                    return -1;
                }
            }
        }

        private static int Icmp(string host, int timeoutMs)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(host, timeoutMs);
                    return reply != null && reply.Status == IPStatus.Success ? (int)Math.Max(1, reply.RoundtripTime) : -1;
                }
            }
            catch (PingException)
            {
                return -1;
            }
        }

        private static IPAddress Resolve(string host)
        {
            IPAddress address;
            if (IPAddress.TryParse(host, out address)) return address;

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                foreach (IPAddress candidate in addresses)
                {
                    if (candidate.AddressFamily == AddressFamily.InterNetwork) return candidate;
                }
                return addresses.Length > 0 ? addresses[0] : null;
            }
            catch (SocketException)
            {
                return null;
            }
        }
    }
}
