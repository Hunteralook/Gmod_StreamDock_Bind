using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace NetSwitchPlugin
{
    internal sealed class ServerEntry
    {
        public string Name;
        public string Host;
        public int Port;
        public bool Udp;
    }

    internal sealed class SubscriptionInfo
    {
        public string Title = string.Empty;
        public long Upload;
        public long Download;
        public long Total;
        public long Expire;
        public List<ServerEntry> Servers = new List<ServerEntry>();
        public string Error;
        public DateTime FetchedUtc;
    }

    // Downloads a subscription the same way proxy clients do and extracts the server list
    // and the subscription-userinfo data (traffic and expiry). Happ itself is not touched:
    // it has no interface for updating its subscriptions from outside.
    internal static class Subscription
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, SubscriptionInfo> Cache = new Dictionary<string, SubscriptionInfo>(StringComparer.Ordinal);
        private static readonly string[] UdpSchemes = { "hysteria2", "hy2", "hysteria", "tuic", "wireguard", "wg" };

        static Subscription()
        {
            // .NET Framework 4.x enables only old TLS versions by default for this kind of executable.
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288);
            }
            catch (NotSupportedException)
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            }
        }

        public static bool IsValidUrl(string url)
        {
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        // Returns the cached subscription while it is fresh; force downloads it again.
        // Downloads of one URL are serialized, so buttons sharing a subscription fetch it once.
        public static SubscriptionInfo Get(string url, bool force)
        {
            lock (Gate)
            {
                SubscriptionInfo cached;
                bool hasCached = Cache.TryGetValue(url, out cached);
                if (!force && hasCached && DateTime.UtcNow - cached.FetchedUtc < MaxAge) return cached;

                SubscriptionInfo fresh = Download(url);
                if (fresh.Error != null && hasCached && cached.Servers.Count > 0)
                {
                    // Keep showing the last good data, but report the failed update.
                    cached.Error = fresh.Error;
                    cached.FetchedUtc = DateTime.UtcNow;
                    return cached;
                }

                Cache[url] = fresh;
                return fresh;
            }
        }

        public static SubscriptionInfo Peek(string url)
        {
            lock (Gate)
            {
                SubscriptionInfo cached;
                return Cache.TryGetValue(url, out cached) ? cached : null;
            }
        }

        private static SubscriptionInfo Download(string url)
        {
            SubscriptionInfo info = new SubscriptionInfo();
            info.FetchedUtc = DateTime.UtcNow;
            if (!IsValidUrl(url))
            {
                info.Error = "ссылка должна начинаться с https://";
                return info;
            }

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                // Panels return a plain list of links to v2rayN-compatible clients.
                request.UserAgent = "v2rayN/7.0";
                request.Timeout = 20000;
                request.ReadWriteTimeout = 20000;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    SubscriptionInfo parsed = Parse(
                        reader.ReadToEnd(),
                        response.Headers["subscription-userinfo"],
                        response.Headers["profile-title"]);
                    parsed.FetchedUtc = info.FetchedUtc;
                    Log.Write("Subscription downloaded: " + parsed.Servers.Count + " server(s).");
                    return parsed;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                info.Error = response != null
                    ? "сервер ответил " + (int)response.StatusCode
                    : "нет связи с сервером подписки";
                Log.Write("Subscription download failed: " + ex.Status + " " + ex.Message);
            }
            catch (Exception ex)
            {
                info.Error = "не удалось разобрать подписку";
                Log.Write("Subscription parsing failed: " + ex.Message);
            }
            return info;
        }

        public static SubscriptionInfo Parse(string body, string userInfoHeader, string titleHeader)
        {
            SubscriptionInfo info = new SubscriptionInfo();
            ApplyUserInfo(info, userInfoHeader);
            if (!string.IsNullOrEmpty(titleHeader)) info.Title = DecodeTitle(titleHeader);

            string text = (body ?? string.Empty).Trim();
            if (text.StartsWith("[") || text.StartsWith("{"))
            {
                ParseJson(info, text);
                if (info.Servers.Count == 0) info.Error = "в подписке нет серверов";
                return info;
            }

            if (!text.Contains("://"))
            {
                string decoded = DecodeBase64(text);
                if (decoded != null) text = decoded;
            }

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("#"))
                {
                    string meta = line.TrimStart('#').Trim();
                    if (meta.StartsWith("subscription-userinfo:", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyUserInfo(info, meta.Substring("subscription-userinfo:".Length));
                    }
                    else if (meta.StartsWith("profile-title:", StringComparison.OrdinalIgnoreCase) && info.Title.Length == 0)
                    {
                        info.Title = DecodeTitle(meta.Substring("profile-title:".Length));
                    }
                    continue;
                }

                if (line.StartsWith("happ://crypt", StringComparison.OrdinalIgnoreCase))
                {
                    info.Error = "подписка зашифрована (happ://crypt), серверы недоступны";
                    continue;
                }

                ServerEntry server = ParseLink(line);
                if (server != null) info.Servers.Add(server);
            }

            if (info.Servers.Count == 0 && info.Error == null) info.Error = "в подписке нет серверов";
            return info;
        }

        public static void ApplyUserInfo(SubscriptionInfo info, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (string part in value.Split(';'))
            {
                string[] pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2) continue;

                long number;
                if (!long.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    double real;
                    if (!double.TryParse(pair[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out real)) continue;
                    number = (long)real;
                }

                switch (pair[0].Trim().ToLowerInvariant())
                {
                    case "upload": info.Upload = number; break;
                    case "download": info.Download = number; break;
                    case "total": info.Total = number; break;
                    case "expire": info.Expire = number; break;
                }
            }
        }

        private static string DecodeTitle(string value)
        {
            string text = value.Trim();
            if (text.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            {
                string decoded = DecodeBase64(text.Substring("base64:".Length));
                if (decoded != null) return decoded.Trim();
            }
            return text;
        }

        public static string DecodeBase64(string value)
        {
            string text = (value ?? string.Empty).Trim().Replace("\r", string.Empty).Replace("\n", string.Empty)
                .Replace('-', '+').Replace('_', '/');
            text = text.TrimEnd('=');
            switch (text.Length % 4)
            {
                case 2: text += "=="; break;
                case 3: text += "="; break;
                case 1: return null;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static ServerEntry ParseLink(string link)
        {
            int schemeEnd = link.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0) return null;
            string scheme = link.Substring(0, schemeEnd).ToLowerInvariant();

            string body = link.Substring(schemeEnd + 3);
            string name = string.Empty;
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                name = SafeUnescape(body.Substring(hash + 1));
                body = body.Substring(0, hash);
            }

            if (scheme == "vmess") return ParseVmess(body);

            if (scheme == "ss" && body.IndexOf('@') < 0)
            {
                // Legacy form: ss://base64(method:password@host:port)
                string decoded = DecodeBase64(body.Split('?')[0].TrimEnd('/'));
                if (decoded == null) return null;
                body = decoded;
            }

            Uri uri;
            if (!Uri.TryCreate("proxy://" + body, UriKind.Absolute, out uri) || uri.Port <= 0) return null;

            ServerEntry server = new ServerEntry();
            server.Host = uri.DnsSafeHost;
            server.Port = uri.Port;
            server.Udp = Array.IndexOf(UdpSchemes, scheme) >= 0;
            server.Name = name.Length > 0 ? name : server.Host;
            return server.Host.Length > 0 ? server : null;
        }

        private static ServerEntry ParseVmess(string body)
        {
            string decoded = DecodeBase64(body);
            if (decoded == null) return null;

            Dictionary<string, object> config = new JavaScriptSerializer().DeserializeObject(decoded) as Dictionary<string, object>;
            if (config == null) return null;

            int port;
            if (!int.TryParse(Json.GetString(config, "port", string.Empty), out port)) return null;

            ServerEntry server = new ServerEntry();
            server.Host = Json.GetTrimmed(config, "add");
            server.Port = port;
            server.Name = Json.GetTrimmed(config, "ps");
            if (server.Name.Length == 0) server.Name = server.Host;
            return server.Host.Length > 0 ? server : null;
        }

        // Some panels give Happ a JSON array of full Xray configs instead of links.
        private static void ParseJson(SubscriptionInfo info, string text)
        {
            object root = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(text);
            IEnumerable configs = root is Dictionary<string, object> ? new[] { root } : root as IEnumerable;
            if (configs == null) return;

            foreach (object item in configs)
            {
                Dictionary<string, object> config = item as Dictionary<string, object>;
                if (config == null) continue;

                IEnumerable outbounds = config.ContainsKey("outbounds") ? config["outbounds"] as IEnumerable : null;
                if (outbounds == null) continue;

                foreach (object outboundItem in outbounds)
                {
                    Dictionary<string, object> outbound = outboundItem as Dictionary<string, object>;
                    if (outbound == null) continue;

                    string protocol = Json.GetString(outbound, "protocol", string.Empty).ToLowerInvariant();
                    if (protocol == "freedom" || protocol == "blackhole" || protocol == "dns") continue;

                    Dictionary<string, object> endpoint = FirstEndpoint(Json.GetDictionary(outbound, "settings"));
                    int port;
                    if (endpoint == null || !int.TryParse(Json.GetString(endpoint, "port", string.Empty), out port)) continue;

                    ServerEntry server = new ServerEntry();
                    server.Host = Json.GetTrimmed(endpoint, "address");
                    server.Port = port;
                    server.Udp = protocol == "hysteria2" || protocol == "hysteria" || protocol == "wireguard";
                    server.Name = Json.GetTrimmed(config, "remarks");
                    if (server.Name.Length == 0) server.Name = server.Host;
                    if (server.Host.Length > 0) info.Servers.Add(server);
                    break;
                }
            }
        }

        private static Dictionary<string, object> FirstEndpoint(Dictionary<string, object> settings)
        {
            foreach (string key in new[] { "vnext", "servers", "peers" })
            {
                IEnumerable list = settings.ContainsKey(key) ? settings[key] as IEnumerable : null;
                if (list == null) continue;
                foreach (object entry in list)
                {
                    Dictionary<string, object> endpoint = entry as Dictionary<string, object>;
                    if (endpoint != null) return endpoint;
                }
            }
            return settings.ContainsKey("address") ? settings : null;
        }

        private static string SafeUnescape(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value).Trim();
            }
            catch (UriFormatException)
            {
                return value.Trim();
            }
        }
    }
}
