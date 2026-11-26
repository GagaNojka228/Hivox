using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using Hivox.Models;

namespace Hivox.Services
{
    public class SubscriptionMetadata
    {
        public string ProfileTitle { get; set; } = "";
        public long UploadBytes { get; set; }
        public long DownloadBytes { get; set; }
        public long TotalBytes { get; set; }
        public long ExpireUnix { get; set; }
        public int UpdateIntervalHours { get; set; }
        public long LastUpdatedUnix { get; set; }
        public string SupportUrl { get; set; } = "";
        public string WebPageUrl { get; set; } = "";
        public string AnnounceText { get; set; } = "";
        public string CustomName { get; set; } = "";
        public bool NeverCollapse { get; set; } = false;
        public int CustomUpdateIntervalMinutes { get; set; } = 0;
        public string CustomDns { get; set; } = "";
        public string CustomSort { get; set; } = "";

        public bool HasTrafficInfo => TotalBytes > 0 || UploadBytes > 0 || DownloadBytes > 0;

        public string FormattedTraffic
        {
            get
            {
                long used = UploadBytes + DownloadBytes;
                if (TotalBytes > 0)
                    return $"{NetworkUtils.FormatBytes(used)} / {NetworkUtils.FormatBytes(TotalBytes)}";
                if (used > 0)
                    return NetworkUtils.FormatBytes(used);
                return "";
            }
        }

        public string FormattedTrafficWithInfinity
        {
            get
            {
                long used = UploadBytes + DownloadBytes;
                if (TotalBytes > 0)
                    return $"{NetworkUtils.FormatBytes(used)} / {NetworkUtils.FormatBytes(TotalBytes)}";
                if (used > 0)
                    return $"{NetworkUtils.FormatBytes(used)} / \u221E";
                return "";
            }
        }

        public double TrafficRatio => TotalBytes > 0
            ? Math.Clamp((double)(UploadBytes + DownloadBytes) / TotalBytes, 0.0, 1.0)
            : 0.0;

        public string FormattedExpire
        {
            get
            {
                if (ExpireUnix <= 0) return "";
                try
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(ExpireUnix).LocalDateTime;
                    var days = (dt.Date - DateTime.Now.Date).Days;
                    if (days < 0) return $"Истекла ({dt:dd.MM.yyyy})";
                    if (days == 0) return "Истекает сегодня";
                    return $"До {dt:dd.MM.yyyy} ({days} дн.)";
                }
                catch { return ""; }
            }
        }

        public string FormattedShortExpire
        {
            get
            {
                if (ExpireUnix <= 0) return "";
                try
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(ExpireUnix).LocalDateTime;
                    var days = (dt.Date - DateTime.Now.Date).Days;
                    if (days < 0) return "Истекла";
                    if (days == 0) return "Сегодня";
                    return $"Осталось {days} д";
                }
                catch { return ""; }
            }
        }

        public string FormattedUpdateInterval
        {
            get
            {
                if (CustomUpdateIntervalMinutes > 0)
                    return CustomUpdateIntervalMinutes < 60
                        ? $"{CustomUpdateIntervalMinutes} м"
                        : $"{CustomUpdateIntervalMinutes / 60} ч";
                return UpdateIntervalHours > 0 ? $"{UpdateIntervalHours} ч" : "";
            }
        }

        public string FormattedLastUpdated => LastUpdatedUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(LastUpdatedUnix).LocalDateTime.ToString("dd.MM.yy, HH:mm")
            : "";
    }

    public static class SubscriptionManager
    {
        public static SubscriptionMetadata LastMetadata { get; private set; }

        public static string GetDeviceHwid()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrEmpty(guid))
                {
                    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(guid));
                    return Convert.ToHexString(hash)[..32].ToLowerInvariant();
                }
            }
            catch { }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName)))[..32].ToLowerInvariant();
        }

        public static async Task<List<ServerInfo>> LoadSubscription(string url, bool sendHwid = true, int timeoutSec = 15)
        {
            var (servers, meta) = await LoadSubscriptionWithMetadata(url, sendHwid, timeoutSec);
            if (meta != null) LastMetadata = meta;
            return servers;
        }

        private static readonly string[] PreferredUserAgents =
        {
            "v2rayNG/1.9.34",
            "HiddifyNext/2.0.5",
            "Hivox/2.0"
        };

        public static async Task<(List<ServerInfo> Servers, SubscriptionMetadata Metadata)> LoadSubscriptionWithMetadata(
            string url, bool sendHwid = true, int timeoutSec = 15)
        {
            string cleanUrl = url.Contains('#') ? url.Split('#')[0].Trim() : url.Trim();
            if (!cleanUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Invalid URL format");

            Exception lastError = null;
            for (int i = 0; i < PreferredUserAgents.Length; i++)
            {
                try
                {
                    var (servers, meta) = await FetchSubscriptionWithUserAgentAsync(cleanUrl, PreferredUserAgents[i], sendHwid, timeoutSec);
                    if (servers.Count > 0)
                        return (ServerParser.DeduplicateNames(servers), meta);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Logger.Log($"Subscription fetch ({PreferredUserAgents[i]}) failed: {ex.Message}");
                    // Do not burn retries on network/timeout errors unless the server actively returned an HTTP status rejection
                    if (ex is TaskCanceledException || (ex is HttpRequestException hre && hre.StatusCode == null))
                        throw;
                }
            }

            if (lastError != null)
                throw lastError;

            return (new List<ServerInfo>(), new SubscriptionMetadata());
        }

        private static async Task<(List<ServerInfo> Servers, SubscriptionMetadata Metadata)> FetchSubscriptionWithUserAgentAsync(
            string cleanUrl, string userAgent, bool sendHwid, int timeoutSec)
        {
            try
            {
                return await SendSubscriptionRequestAsync(cleanUrl, userAgent, sendHwid, timeoutSec, useLocalProxy: false);
            }
            catch (HttpRequestException)
            {
                return await SendSubscriptionRequestAsync(cleanUrl, userAgent, sendHwid, timeoutSec, useLocalProxy: true);
            }
        }

        private static async Task<(List<ServerInfo> Servers, SubscriptionMetadata Metadata)> SendSubscriptionRequestAsync(
            string cleanUrl, string userAgent, bool sendHwid, int timeoutSec, bool useLocalProxy)
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
                UseProxy = useLocalProxy,
                Proxy = useLocalProxy ? Config.CreateLocalHttpProxy() : null
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSec))
            };

            var request = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            request.Headers.Add("Accept", "text/plain, application/json, application/yaml, */*");
            request.Headers.Add("Cache-Control", "no-cache");

            if (sendHwid)
            {
                request.Headers.TryAddWithoutValidation("x-hwid", GetDeviceHwid());
                request.Headers.TryAddWithoutValidation("x-device-os", "Windows");
                request.Headers.TryAddWithoutValidation("x-ver-os", Environment.OSVersion.Version.ToString());
                request.Headers.TryAddWithoutValidation("x-device-model", Environment.MachineName);
            }

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var meta = ParseHeaders(response);
            string content = (await response.Content.ReadAsStringAsync()).Trim('\uFEFF', ' ', '\t', '\r', '\n');
            if (string.IsNullOrEmpty(content))
                throw new Exception("Empty response");

            var servers = ParseSubscriptionBody(content, meta);
            return (servers, meta);
        }

        private static List<ServerInfo> ParseSubscriptionBody(string content, SubscriptionMetadata meta)
        {
            var lines = DecodeSubscriptionContent(content);
            if (meta != null)
                ParseBodyMetadata(lines, meta);

            var servers = new List<ServerInfo>();
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//")) continue;
                if (line.StartsWith("- ")) line = line[2..].Trim();

                if (Config.SupportedProtocols.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    if (ValidateServerUri(line))
                        servers.Add(ServerParser.ParseServer(line));
                }
            }

            if (servers.Count > 0)
                return servers;

            // Fallback 1: Sing-box / Xray JSON subscription
            string joined = string.Join("\n", lines).Trim();
            if (joined.StartsWith("{") || joined.StartsWith("["))
            {
                var jsonServers = TryParseJsonSubscription(joined);
                if (jsonServers.Count > 0) return jsonServers;
            }

            // Fallback 2: Clash / Mihomo YAML subscription
            if (joined.Contains("proxies:", StringComparison.OrdinalIgnoreCase))
            {
                var clashServers = TryParseClashYaml(lines);
                if (clashServers.Count > 0) return clashServers;
            }

            // Fallback 3: Extract any embedded protocol URIs from mixed text
            var uris = Regex.Matches(joined, @"(?:vmess|vless|trojan|ss|hysteria2|hy2|hysteria|tuic|socks5|socks|wireguard|wg|anytls|ssh)://[^\s""'<>]+", RegexOptions.IgnoreCase);
            foreach (Match m in uris)
            {
                if (ValidateServerUri(m.Value))
                    servers.Add(ServerParser.ParseServer(m.Value));
            }

            return servers;
        }

        private static bool TryDecodeBase64Text(string input, out string decoded)
        {
            decoded = "";
            try
            {
                string compact = Regex.Replace(input, @"\s+", "").Replace('-', '+').Replace('_', '/');
                if (compact.Length < 8) return false;
                string padded = compact.PadRight((compact.Length + 3) / 4 * 4, '=');
                byte[] bytes = Convert.FromBase64String(padded);
                decoded = Encoding.UTF8.GetString(bytes).Trim('\uFEFF', '\0', ' ', '\t', '\r', '\n');
                return !string.IsNullOrEmpty(decoded);
            }
            catch { return false; }
        }

        private static List<string> DecodeSubscriptionContent(string content)
        {
            string current = content.Trim('\uFEFF', ' ', '\t', '\r', '\n');

            // Decode up to 2 layers of Base64 if content does not already look like plain URIs, JSON, or YAML
            for (int depth = 0; depth < 2; depth++)
            {
                if (current.StartsWith("{") || current.StartsWith("[") ||
                    current.Contains("proxies:", StringComparison.OrdinalIgnoreCase))
                    break;

                var existingLines = current.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                if (existingLines.Any(l => Config.SupportedProtocols.Any(p => l.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
                    return existingLines;

                if (TryDecodeBase64Text(current, out string decoded))
                    current = decoded;
                else
                    break;
            }

            return current.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        public static bool IsProxyOutbound(JObject ob)
        {
            string type = (ob?["type"]?.ToString() ?? ob?["protocol"]?.ToString() ?? "").ToLowerInvariant();
            return type is not ("" or "direct" or "block" or "dns" or "freedom" or "blackhole" or "selector" or "urltest");
        }

        private static List<ServerInfo> TryParseJsonSubscription(string jsonText)
        {
            var servers = new List<ServerInfo>();
            try
            {
                var token = JToken.Parse(jsonText);
                if (token is JArray arr)
                {
                    foreach (var doc in arr.OfType<JObject>())
                    {
                        if (doc["outbounds"] is JArray)
                        {
                            var profile = ParseJsonProfileDoc(doc);
                            if (profile != null)
                                servers.Add(profile);
                        }
                        else
                        {
                            string docRemarks = doc["remarks"]?.ToString() ?? doc["ps"]?.ToString() ?? doc["name"]?.ToString() ?? "";
                            string uri = OutboundJsonToUri(doc, docRemarks);
                            if (!string.IsNullOrEmpty(uri))
                                servers.Add(ServerParser.ParseServer(uri));
                        }
                    }
                }
                else if (token is JObject singleObj)
                {
                    if (singleObj["outbounds"] is JArray outbounds)
                    {
                        var proxyObs = outbounds.OfType<JObject>().Where(IsProxyOutbound).ToList();
                        bool isSingleProfileDoc =
                            singleObj["remarks"] != null ||
                            singleObj["ps"] != null ||
                            singleObj["observatory"] != null ||
                            singleObj["burstObservatory"] != null ||
                            singleObj["routing"]?["balancers"] != null ||
                            proxyObs.Count <= 1;

                        if (isSingleProfileDoc)
                        {
                            var profile = ParseJsonProfileDoc(singleObj);
                            if (profile != null)
                                servers.Add(profile);
                        }
                        else
                        {
                            // Sing-box / multi-node JSON outbound list (each outbound is a separate server)
                            foreach (var ob in proxyObs)
                            {
                                string uri = OutboundJsonToUri(ob, "");
                                if (!string.IsNullOrEmpty(uri))
                                    servers.Add(ServerParser.ParseServer(uri));
                            }
                        }
                    }
                    else
                    {
                        string docRemarks = singleObj["remarks"]?.ToString() ?? singleObj["ps"]?.ToString() ?? singleObj["name"]?.ToString() ?? "";
                        string uri = OutboundJsonToUri(singleObj, docRemarks);
                        if (!string.IsNullOrEmpty(uri))
                            servers.Add(ServerParser.ParseServer(uri));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"JSON subscription parse warning: {ex.Message}");
            }
            return servers;
        }

        private static ServerInfo ParseJsonProfileDoc(JObject doc)
        {
            var outbounds = doc["outbounds"] as JArray;
            if (outbounds == null) return null;

            var proxyObs = outbounds.OfType<JObject>().Where(IsProxyOutbound).ToList();
            if (proxyObs.Count == 0) return null;

            var primary = proxyObs.FirstOrDefault(o =>
                string.Equals(o["tag"]?.ToString(), "proxy", StringComparison.OrdinalIgnoreCase)) ?? proxyObs[0];

            string docRemarks = doc["remarks"]?.ToString() ?? doc["ps"]?.ToString() ?? doc["name"]?.ToString() ?? "";
            string uri = OutboundJsonToUri(primary, docRemarks);
            if (string.IsNullOrEmpty(uri))
            {
                foreach (var alt in proxyObs)
                {
                    uri = OutboundJsonToUri(alt, docRemarks);
                    if (!string.IsNullOrEmpty(uri)) break;
                }
            }
            if (string.IsNullOrEmpty(uri)) return null;

            var server = ServerParser.ParseServer(uri);
            server.RawJsonConfig = doc.ToString(Newtonsoft.Json.Formatting.None);
            if (proxyObs.Count > 1 && !string.IsNullOrEmpty(server.Details))
                server.Details = $"{server.Details} • {proxyObs.Count} узл.";
            return server;
        }

        public static string OutboundJsonToUri(JObject ob, string fallbackName)
        {
            string type = (ob["type"]?.ToString() ?? ob["protocol"]?.ToString() ?? "").ToLowerInvariant();
            if (type is "" or "direct" or "block" or "dns" or "freedom" or "blackhole" or "selector" or "urltest")
                return "";

            string tag = ob["tag"]?.ToString();
            string name = !string.IsNullOrWhiteSpace(fallbackName)
                ? fallbackName
                : (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("proxy", StringComparison.OrdinalIgnoreCase) ? tag : "");
            string frag = string.IsNullOrWhiteSpace(name) ? "" : $"#{Uri.EscapeDataString(name)}";

            // Sing-box flat format
            if (ob["server"] != null)
            {
                string server = ob["server"]?.ToString() ?? "";
                int port = ob["server_port"]?.ToObject<int>() ?? 443;
                string hostPart = server.Contains(':') ? $"[{server}]:{port}" : $"{server}:{port}";
                string sni = ob["tls"]?["server_name"]?.ToString() ?? "";
                bool tlsEnabled = ob["tls"]?["enabled"]?.ToObject<bool>() ?? false;
                bool insecure = ob["tls"]?["insecure"]?.ToObject<bool>() ?? false;
                string fp = ob["tls"]?["utls"]?["fingerprint"]?.ToString() ?? "";

                if (type == "vless")
                {
                    string uuid = ob["uuid"]?.ToString() ?? "";
                    var q = new List<string>();
                    bool isReality = ob["tls"]?["reality"]?["enabled"]?.ToObject<bool>() ?? false;
                    q.Add($"security={(isReality ? "reality" : tlsEnabled ? "tls" : "none")}");
                    if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                    if (!string.IsNullOrEmpty(fp)) q.Add($"fp={Uri.EscapeDataString(fp)}");
                    if (isReality)
                    {
                        q.Add($"pbk={Uri.EscapeDataString(ob["tls"]?["reality"]?["public_key"]?.ToString() ?? "")}");
                        q.Add($"sid={Uri.EscapeDataString(ob["tls"]?["reality"]?["short_id"]?.ToString() ?? "")}");
                    }
                    string flow = ob["flow"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(flow)) q.Add($"flow={Uri.EscapeDataString(flow)}");
                    string tr = ob["transport"]?["type"]?.ToString() ?? "tcp";
                    q.Add($"type={tr}");
                    if (ob["transport"]?["path"] != null) q.Add($"path={Uri.EscapeDataString(ob["transport"]["path"].ToString())}");
                    if (ob["transport"]?["service_name"] != null) q.Add($"serviceName={Uri.EscapeDataString(ob["transport"]["service_name"].ToString())}");
                    if (insecure) q.Add("allowInsecure=1");
                    return $"vless://{uuid}@{hostPart}?{string.Join("&", q)}{frag}";
                }
                if (type == "vmess")
                {
                    var vmessObj = new JObject
                    {
                        ["v"] = "2",
                        ["ps"] = name,
                        ["add"] = server,
                        ["port"] = port.ToString(),
                        ["id"] = ob["uuid"]?.ToString() ?? "",
                        ["aid"] = (ob["alter_id"]?.ToObject<int>() ?? 0).ToString(),
                        ["scy"] = ob["security"]?.ToString() ?? "auto",
                        ["net"] = ob["transport"]?["type"]?.ToString() ?? "tcp",
                        ["path"] = ob["transport"]?["path"]?.ToString() ?? ob["transport"]?["service_name"]?.ToString() ?? "/",
                        ["tls"] = tlsEnabled ? "tls" : "",
                        ["sni"] = sni
                    };
                    return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessObj.ToString(Newtonsoft.Json.Formatting.None)));
                }
                if (type == "trojan")
                {
                    string pw = Uri.EscapeDataString(ob["password"]?.ToString() ?? "");
                    var q = new List<string> { "security=tls" };
                    if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                    if (insecure) q.Add("allowInsecure=1");
                    return $"trojan://{pw}@{hostPart}?{string.Join("&", q)}{frag}";
                }
                if (type == "shadowsocks")
                {
                    string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ob["method"]}:{ob["password"]}"));
                    return $"ss://{cred}@{hostPart}{frag}";
                }
                if (type is "hysteria2" or "hysteria")
                {
                    string pw = Uri.EscapeDataString(ob["password"]?.ToString() ?? ob["auth_str"]?.ToString() ?? "");
                    var q = new List<string>();
                    if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                    if (insecure) q.Add("insecure=1");
                    string qs = q.Count > 0 ? "?" + string.Join("&", q) : "";
                    string sch = type == "hysteria" ? "hysteria" : "hysteria2";
                    return $"{sch}://{pw}@{hostPart}{qs}{frag}";
                }
                if (type == "tuic")
                {
                    string uuid = ob["uuid"]?.ToString() ?? "";
                    var q = new List<string> { $"password={Uri.EscapeDataString(ob["password"]?.ToString() ?? "")}" };
                    if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                    if (insecure) q.Add("allow_insecure=1");
                    return $"tuic://{uuid}@{hostPart}?{string.Join("&", q)}{frag}";
                }
                if (type == "anytls")
                {
                    string pw = Uri.EscapeDataString(ob["password"]?.ToString() ?? "");
                    var q = new List<string>();
                    if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                    if (insecure) q.Add("insecure=1");
                    string qs = q.Count > 0 ? "?" + string.Join("&", q) : "";
                    return $"anytls://{pw}@{hostPart}{qs}{frag}";
                }
                if (type == "wireguard")
                {
                    string pk = Uri.EscapeDataString(ob["private_key"]?.ToString() ?? "");
                    return $"wireguard://{pk}@{hostPart}{frag}";
                }
                if (type == "socks")
                {
                    string u = ob["username"]?.ToString() ?? "";
                    string p = ob["password"]?.ToString() ?? "";
                    string auth = !string.IsNullOrEmpty(u) ? $"{Uri.EscapeDataString(u)}:{Uri.EscapeDataString(p)}@" : "";
                    return $"socks://{auth}{hostPart}{frag}";
                }
            }

            // Xray nested format (vnext)
            if (ob["settings"]?["vnext"] is JArray vnext && vnext.Count > 0)
            {
                var vn = vnext[0];
                string server = vn["address"]?.ToString() ?? "";
                int port = vn["port"]?.ToObject<int>() ?? 443;
                string hostPart = server.Contains(':') ? $"[{server}]:{port}" : $"{server}:{port}";
                var user = vn["users"]?[0];
                string uuid = user?["id"]?.ToString() ?? "";
                var ss = ob["streamSettings"];
                string net = ss?["network"]?.ToString() ?? "tcp";
                string sec = ss?["security"]?.ToString() ?? "none";

                if (type == "vless")
                {
                    var q = new List<string> { $"type={net}", $"security={sec}" };
                    string flow = user?["flow"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(flow)) q.Add($"flow={Uri.EscapeDataString(flow)}");
                    if (sec == "reality")
                    {
                        var rs = ss?["realitySettings"];
                        q.Add($"sni={Uri.EscapeDataString(rs?["serverName"]?.ToString() ?? "")}");
                        q.Add($"pbk={Uri.EscapeDataString(rs?["publicKey"]?.ToString() ?? "")}");
                        q.Add($"sid={Uri.EscapeDataString(rs?["shortId"]?.ToString() ?? "")}");
                        q.Add($"fp={Uri.EscapeDataString(rs?["fingerprint"]?.ToString() ?? "chrome")}");
                        string spx = rs?["spiderX"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(spx)) q.Add($"spx={Uri.EscapeDataString(spx)}");
                    }
                    else if (sec == "tls")
                    {
                        var ts = ss?["tlsSettings"];
                        q.Add($"sni={Uri.EscapeDataString(ts?["serverName"]?.ToString() ?? server)}");
                        string fp = ts?["fingerprint"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(fp)) q.Add($"fp={Uri.EscapeDataString(fp)}");
                        if (ts?["allowInsecure"]?.ToObject<bool>() == true) q.Add("allowInsecure=1");
                    }
                    AppendXrayTransportQuery(ss, net, q);
                    return $"vless://{uuid}@{hostPart}?{string.Join("&", q)}{frag}";
                }
                if (type == "vmess")
                {
                    string path = ss?["wsSettings"]?["path"]?.ToString()
                        ?? ss?["grpcSettings"]?["serviceName"]?.ToString()
                        ?? ss?["xhttpSettings"]?["path"]?.ToString()
                        ?? ss?["httpSettings"]?["path"]?.ToString()
                        ?? "/";
                    string wsHost = ss?["wsSettings"]?["headers"]?["Host"]?.ToString()
                        ?? ss?["xhttpSettings"]?["host"]?.ToString()
                        ?? "";
                    var vmessObj = new JObject
                    {
                        ["v"] = "2",
                        ["ps"] = name,
                        ["add"] = server,
                        ["port"] = port.ToString(),
                        ["id"] = uuid,
                        ["aid"] = (user?["alterId"]?.ToObject<int>() ?? 0).ToString(),
                        ["scy"] = user?["security"]?.ToString() ?? "auto",
                        ["net"] = net,
                        ["path"] = path,
                        ["host"] = wsHost,
                        ["tls"] = sec == "tls" ? "tls" : "",
                        ["sni"] = ss?["tlsSettings"]?["serverName"]?.ToString() ?? ""
                    };
                    return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessObj.ToString(Newtonsoft.Json.Formatting.None)));
                }
            }

            // Xray nested format (servers: trojan, shadowsocks, socks)
            if (ob["settings"]?["servers"] is JArray srvs && srvs.Count > 0)
            {
                var sv = srvs[0];
                string server = sv["address"]?.ToString() ?? "";
                int port = sv["port"]?.ToObject<int>() ?? 443;
                string hostPart = server.Contains(':') ? $"[{server}]:{port}" : $"{server}:{port}";
                var ss = ob["streamSettings"];
                string net = ss?["network"]?.ToString() ?? "tcp";
                string sec = ss?["security"]?.ToString() ?? "none";

                if (type == "trojan")
                {
                    string pw = Uri.EscapeDataString(sv["password"]?.ToString() ?? "");
                    var q = new List<string> { $"type={net}", $"security={(sec == "none" ? "tls" : sec)}" };
                    string sni = ss?["tlsSettings"]?["serverName"]?.ToString() ?? ss?["realitySettings"]?["serverName"]?.ToString() ?? server;
                    if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                    AppendXrayTransportQuery(ss, net, q);
                    return $"trojan://{pw}@{hostPart}?{string.Join("&", q)}{frag}";
                }
                if (type == "shadowsocks")
                {
                    string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sv["method"]}:{sv["password"]}"));
                    return $"ss://{cred}@{hostPart}{frag}";
                }
                if (type == "socks")
                {
                    var uObj = sv["users"]?[0];
                    string u = uObj?["user"]?.ToString() ?? "";
                    string p = uObj?["pass"]?.ToString() ?? "";
                    string auth = !string.IsNullOrEmpty(u) ? $"{Uri.EscapeDataString(u)}:{Uri.EscapeDataString(p)}@" : "";
                    return $"socks://{auth}{hostPart}{frag}";
                }
            }

            return "";
        }

        private static void AppendXrayTransportQuery(JToken ss, string net, List<string> q)
        {
            if (ss == null) return;
            if (net == "ws" && ss["wsSettings"] is JToken ws)
            {
                if (ws["path"] != null) q.Add($"path={Uri.EscapeDataString(ws["path"].ToString())}");
                if (ws["headers"]?["Host"] != null) q.Add($"host={Uri.EscapeDataString(ws["headers"]["Host"].ToString())}");
            }
            else if (net == "grpc" && ss["grpcSettings"] is JToken grpc)
            {
                if (grpc["serviceName"] != null) q.Add($"serviceName={Uri.EscapeDataString(grpc["serviceName"].ToString())}");
            }
            else if (net == "xhttp" && ss["xhttpSettings"] is JToken xh)
            {
                if (xh["path"] != null) q.Add($"path={Uri.EscapeDataString(xh["path"].ToString())}");
                if (xh["host"] != null) q.Add($"host={Uri.EscapeDataString(xh["host"].ToString())}");
                if (xh["mode"] != null) q.Add($"mode={Uri.EscapeDataString(xh["mode"].ToString())}");
                if (xh["extra"] != null) q.Add($"extra={Uri.EscapeDataString(xh["extra"].ToString(Newtonsoft.Json.Formatting.None))}");
            }
            else if (net is "h2" or "http" && ss["httpSettings"] is JToken hp)
            {
                if (hp["path"] != null) q.Add($"path={Uri.EscapeDataString(hp["path"].ToString())}");
                if (hp["host"]?[0] != null) q.Add($"host={Uri.EscapeDataString(hp["host"][0].ToString())}");
            }
        }

        private static List<ServerInfo> TryParseClashYaml(List<string> lines)
        {
            var servers = new List<ServerInfo>();
            bool inProxies = false;
            var currentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void FlushCurrent()
            {
                if (currentMap.Count == 0) return;
                string uri = ClashDictToUri(currentMap);
                if (!string.IsNullOrEmpty(uri))
                    servers.Add(ServerParser.ParseServer(uri));
                currentMap.Clear();
            }

            foreach (var raw in lines)
            {
                string trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("proxies:", StringComparison.OrdinalIgnoreCase))
                {
                    inProxies = true;
                    continue;
                }
                if (!inProxies) continue;

                // Top-level YAML section ended
                if (!raw.StartsWith(" ") && !raw.StartsWith("\t") && !trimmed.StartsWith("-") && trimmed.EndsWith(":"))
                {
                    FlushCurrent();
                    break;
                }

                // Inline YAML map: - { name: ..., type: vless, ... }
                if (trimmed.StartsWith("- {") && trimmed.EndsWith("}"))
                {
                    FlushCurrent();
                    try
                    {
                        var dict = ParseInlineYamlMap(trimmed[1..].Trim());
                        string uri = ClashDictToUri(dict);
                        if (!string.IsNullOrEmpty(uri))
                            servers.Add(ServerParser.ParseServer(uri));
                    }
                    catch { }
                    continue;
                }

                if (trimmed.StartsWith("- "))
                {
                    FlushCurrent();
                    trimmed = trimmed[2..].Trim();
                }

                int colon = trimmed.IndexOf(':');
                if (colon > 0)
                {
                    string key = trimmed[..colon].Trim();
                    string val = trimmed[(colon + 1)..].Trim().Trim('"', '\'');
                    currentMap[key] = val;
                }
            }
            FlushCurrent();
            return servers;
        }

        private static Dictionary<string, string> ParseInlineYamlMap(string inner)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            inner = inner.Trim();
            if (inner.StartsWith("{") && inner.EndsWith("}"))
                inner = inner[1..^1];

            var parts = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            char quoteChar = '\0';
            int depth = 0;

            foreach (char ch in inner)
            {
                if ((ch == '"' || ch == '\'') && !inQuotes)
                {
                    inQuotes = true;
                    quoteChar = ch;
                }
                else if (inQuotes && ch == quoteChar)
                {
                    inQuotes = false;
                }
                else if (!inQuotes)
                {
                    if (ch is '{' or '[') depth++;
                    else if (ch is '}' or ']') depth--;
                    else if (ch == ',' && depth == 0)
                    {
                        parts.Add(sb.ToString());
                        sb.Clear();
                        continue;
                    }
                }
                sb.Append(ch);
            }
            if (sb.Length > 0) parts.Add(sb.ToString());

            foreach (var part in parts)
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                string k = part[..colon].Trim().Trim('"', '\'');
                string v = part[(colon + 1)..].Trim().Trim('"', '\'');
                dict[k] = v;
            }
            return dict;
        }

        private static string ClashDictToUri(Dictionary<string, string> d)
        {
            if (!d.TryGetValue("type", out string type) || !d.TryGetValue("server", out string server))
                return "";
            type = type.ToLowerInvariant();
            string port = d.GetValueOrDefault("port", "443");
            string hostPart = server.Contains(':') ? $"[{server}]:{port}" : $"{server}:{port}";
            string name = d.GetValueOrDefault("name", "");
            string frag = string.IsNullOrWhiteSpace(name) ? "" : $"#{Uri.EscapeDataString(name)}";
            string sni = d.GetValueOrDefault("servername", d.GetValueOrDefault("sni", ""));
            bool insecure = d.GetValueOrDefault("skip-cert-verify", "false").Equals("true", StringComparison.OrdinalIgnoreCase);

            if (type == "vless")
            {
                string uuid = d.GetValueOrDefault("uuid", "");
                var q = new List<string> { $"type={d.GetValueOrDefault("network", "tcp")}" };
                bool tls = d.GetValueOrDefault("tls", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                string pbk = d.GetValueOrDefault("public-key", "");
                q.Add($"security={(!string.IsNullOrEmpty(pbk) ? "reality" : tls ? "tls" : "none")}");
                if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                if (!string.IsNullOrEmpty(pbk)) q.Add($"pbk={Uri.EscapeDataString(pbk)}");
                if (d.TryGetValue("short-id", out var sid)) q.Add($"sid={Uri.EscapeDataString(sid)}");
                if (d.TryGetValue("flow", out var flow) && !string.IsNullOrEmpty(flow)) q.Add($"flow={Uri.EscapeDataString(flow)}");
                if (d.TryGetValue("client-fingerprint", out var fp)) q.Add($"fp={Uri.EscapeDataString(fp)}");
                if (d.TryGetValue("path", out var path)) q.Add($"path={Uri.EscapeDataString(path)}");
                if (d.TryGetValue("grpc-service-name", out var gs)) q.Add($"serviceName={Uri.EscapeDataString(gs)}");
                if (insecure) q.Add("allowInsecure=1");
                return $"vless://{uuid}@{hostPart}?{string.Join("&", q)}{frag}";
            }
            if (type == "vmess")
            {
                var vmessObj = new JObject
                {
                    ["v"] = "2",
                    ["ps"] = name,
                    ["add"] = server,
                    ["port"] = port,
                    ["id"] = d.GetValueOrDefault("uuid", ""),
                    ["aid"] = d.GetValueOrDefault("alterId", "0"),
                    ["scy"] = d.GetValueOrDefault("cipher", "auto"),
                    ["net"] = d.GetValueOrDefault("network", "tcp"),
                    ["path"] = d.GetValueOrDefault("path", "/"),
                    ["tls"] = d.GetValueOrDefault("tls", "false").Equals("true", StringComparison.OrdinalIgnoreCase) ? "tls" : "",
                    ["sni"] = sni
                };
                return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessObj.ToString(Newtonsoft.Json.Formatting.None)));
            }
            if (type == "trojan")
            {
                string pw = Uri.EscapeDataString(d.GetValueOrDefault("password", ""));
                var q = new List<string> { "security=tls" };
                if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                if (insecure) q.Add("allowInsecure=1");
                return $"trojan://{pw}@{hostPart}?{string.Join("&", q)}{frag}";
            }
            if (type == "ss")
            {
                string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{d.GetValueOrDefault("cipher", "aes-256-gcm")}:{d.GetValueOrDefault("password", "")}"));
                return $"ss://{cred}@{hostPart}{frag}";
            }
            if (type is "hysteria2" or "hy2")
            {
                string pw = Uri.EscapeDataString(d.GetValueOrDefault("password", ""));
                var q = new List<string>();
                if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                if (insecure) q.Add("insecure=1");
                string qs = q.Count > 0 ? "?" + string.Join("&", q) : "";
                return $"hysteria2://{pw}@{hostPart}{qs}{frag}";
            }
            if (type == "tuic")
            {
                string uuid = d.GetValueOrDefault("uuid", "");
                var q = new List<string> { $"password={Uri.EscapeDataString(d.GetValueOrDefault("password", ""))}" };
                if (!string.IsNullOrEmpty(sni)) q.Add($"sni={Uri.EscapeDataString(sni)}");
                if (insecure) q.Add("allow_insecure=1");
                return $"tuic://{uuid}@{hostPart}?{string.Join("&", q)}{frag}";
            }
            return "";
        }

        private static SubscriptionMetadata ParseHeaders(HttpResponseMessage response)
        {
            var meta = new SubscriptionMetadata
            {
                LastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            string GetHeader(string name)
            {
                if (response.Headers.TryGetValues(name, out var vals))
                    return vals.FirstOrDefault() ?? "";
                var match = response.Headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
                return match.Value?.FirstOrDefault() ?? "";
            }

            string titleRaw = GetHeader("profile-title");
            if (!string.IsNullOrEmpty(titleRaw))
                meta.ProfileTitle = DecodeHeaderValue(titleRaw);

            string userInfo = GetHeader("subscription-userinfo");
            if (!string.IsNullOrEmpty(userInfo))
            {
                foreach (var part in userInfo.Split(';'))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2) continue;
                    string k = kv[0].Trim().ToLowerInvariant();
                    if (!long.TryParse(kv[1].Trim(), out long val)) continue;
                    if (k == "upload") meta.UploadBytes = val;
                    else if (k == "download") meta.DownloadBytes = val;
                    else if (k == "total") meta.TotalBytes = val;
                    else if (k == "expire") meta.ExpireUnix = val;
                }
            }

            if (int.TryParse(GetHeader("profile-update-interval"), out int interval) && interval > 0)
                meta.UpdateIntervalHours = interval;

            meta.SupportUrl = GetHeader("support-url");
            meta.WebPageUrl = GetHeader("profile-web-page-url");

            string announce = GetHeader("announce");
            if (!string.IsNullOrEmpty(announce))
                meta.AnnounceText = DecodeHeaderValue(announce);

            return meta;
        }

        private static void ParseBodyMetadata(List<string> lines, SubscriptionMetadata meta)
        {
            foreach (var line in lines)
            {
                if (!line.StartsWith("#")) continue;
                string comment = line.TrimStart('#', ' ');
                if (comment.StartsWith("profile-title:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(meta.ProfileTitle))
                    meta.ProfileTitle = DecodeHeaderValue(comment.Substring(14).Trim());
                else if (comment.StartsWith("announce:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(meta.AnnounceText))
                    meta.AnnounceText = DecodeHeaderValue(comment.Substring(9).Trim());
                else if (comment.StartsWith("profile-update-interval:", StringComparison.OrdinalIgnoreCase) && meta.UpdateIntervalHours == 0)
                {
                    if (int.TryParse(comment.Substring(24).Trim(), out int iv))
                        meta.UpdateIntervalHours = iv;
                }
            }
        }

        private static string DecodeHeaderValue(string raw)
        {
            raw = raw.Trim();
            if (raw.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string b64 = raw.Substring(7).Trim().Replace('-', '+').Replace('_', '/');
                    b64 = b64.PadRight((b64.Length + 3) / 4 * 4, '=');
                    return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                }
                catch { }
            }
            try { return Uri.UnescapeDataString(raw); } catch { return raw; }
        }

        public static List<ServerInfo> ParseMassLinks(string text)
        {
            var servers = new List<ServerInfo>();
            if (string.IsNullOrWhiteSpace(text)) return servers;

            var trimmed = text.Trim('\uFEFF', ' ', '\t', '\r', '\n');
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var url in Regex.Split(trimmed, @"[\s,;]+").Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    try { servers.AddRange(LoadSubscription(url).GetAwaiter().GetResult()); }
                    catch { }
                }
                return ServerParser.DeduplicateNames(servers);
            }

            servers.AddRange(ParseSubscriptionBody(trimmed, null));
            return ServerParser.DeduplicateNames(servers);
        }

        public static bool ValidateServerUri(string uri)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(uri)) return false;
                if (!Config.SupportedProtocols.Any(p => uri.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return false;
                return uri.Length >= 8;
            }
            catch { return false; }
        }
    }
}
