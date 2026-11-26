using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.Models;

namespace Hivox.Mobile.Services;

public static class SubscriptionManager
{
    private static readonly string[] PreferredUserAgents =
    {
        "v2rayNG/1.9.34",
        "HiddifyNext/2.0.5",
        "SaninaVPN/2.0"
    };

    private static string GetDeviceHwid() => SecureVaultService.GetOrCreateHwid();

    public static async Task<List<ServerInfo>> LoadSubscription(string url)
    {
        var (servers, _) = await LoadSubscriptionWithMetadata(url);
        return servers;
    }

    public static async Task<(List<ServerInfo> Servers, SubscriptionMetadata Metadata)> LoadSubscriptionWithMetadata(string url)
    {
        string cleanUrl = url.Contains('#') ? url.Split('#')[0].Trim() : url.Trim();
        if (!cleanUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Неверный формат URL (нужен http:// или https://)");

        var userAgents = new List<string>();
        if (!string.IsNullOrWhiteSpace(Config.CustomUserAgent))
            userAgents.Add(Config.CustomUserAgent.Trim());
        foreach (var ua in PreferredUserAgents)
        {
            if (!userAgents.Contains(ua))
                userAgents.Add(ua);
        }

        Exception? lastError = null;
        for (int i = 0; i < userAgents.Count; i++)
        {
            try
            {
                var (servers, meta) = await FetchWithUserAgentAsync(cleanUrl, userAgents[i]);
                if (servers.Count > 0)
                    return (ServerParser.DeduplicateNames(servers), meta);
            }
            catch (Exception ex)
            {
                lastError = ex;
                Logger.Log($"Subscription fetch ({userAgents[i]}) failed: {ex.Message}");
                if (ex is TaskCanceledException || (ex is HttpRequestException hre && hre.StatusCode == null))
                    throw;
            }
        }

        if (lastError != null)
            throw lastError;

        return (new List<ServerInfo>(), new SubscriptionMetadata());
    }

    private static async Task<(List<ServerInfo> Servers, SubscriptionMetadata Metadata)> FetchWithUserAgentAsync(
        string cleanUrl, string userAgent)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, Config.RequestTimeout))
        };

        var request = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.Add("Accept", "text/plain, application/json, application/yaml, */*");
        request.Headers.Add("Cache-Control", "no-cache");
        if (Config.SendHwid)
        {
            request.Headers.TryAddWithoutValidation("x-hwid", GetDeviceHwid());
            request.Headers.TryAddWithoutValidation("x-device-os", "Android");
            request.Headers.TryAddWithoutValidation("x-ver-os", global::Android.OS.Build.VERSION.Release ?? "14");
            request.Headers.TryAddWithoutValidation("x-device-model", global::Android.OS.Build.Model ?? "Android");
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var meta = ParseHeaders(response);
        string content = (await response.Content.ReadAsStringAsync()).Trim('\uFEFF', ' ', '\t', '\r', '\n');
        if (string.IsNullOrEmpty(content))
            throw new Exception("Пустой ответ от сервера подписки");

        var servers = ParseSubscriptionBody(content, meta);
        return (servers, meta);
    }

    private static List<ServerInfo> ParseSubscriptionBody(string content, SubscriptionMetadata? meta)
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

        string joined = string.Join("\n", lines).Trim();
        if (joined.StartsWith("{") || joined.StartsWith("["))
        {
            var jsonServers = TryParseJsonSubscription(joined);
            if (jsonServers.Count > 0) return jsonServers;
        }

        if (joined.Contains("proxies:", StringComparison.OrdinalIgnoreCase))
        {
            var clashServers = TryParseClashYaml(lines);
            if (clashServers.Count > 0) return clashServers;
        }

        var uris = Regex.Matches(joined, @"(?:vmess|vless|trojan|ss|hysteria2|hy2|socks5|socks)://[^\s""'<>]+", RegexOptions.IgnoreCase);
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

    public static bool IsProxyOutbound(JObject? ob)
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
                        if (profile != null) servers.Add(profile);
                    }
                    else
                    {
                        string docRemarks = doc["remarks"]?.ToString() ?? doc["ps"]?.ToString() ?? doc["name"]?.ToString() ?? "";
                        string uri = OutboundJsonToUri(doc, docRemarks);
                        if (!string.IsNullOrEmpty(uri)) servers.Add(ServerParser.ParseServer(uri));
                    }
                }
            }
            else if (token is JObject singleObj)
            {
                if (singleObj["outbounds"] is JArray outbounds)
                {
                    var proxyObs = outbounds.OfType<JObject>().Where(IsProxyOutbound).ToList();
                    bool isSingle = singleObj["remarks"] != null || singleObj["ps"] != null || proxyObs.Count <= 1;
                    if (isSingle)
                    {
                        var profile = ParseJsonProfileDoc(singleObj);
                        if (profile != null) servers.Add(profile);
                    }
                    else
                    {
                        foreach (var ob in proxyObs)
                        {
                            string uri = OutboundJsonToUri(ob, "");
                            if (!string.IsNullOrEmpty(uri)) servers.Add(ServerParser.ParseServer(uri));
                        }
                    }
                }
                else
                {
                    string docRemarks = singleObj["remarks"]?.ToString() ?? singleObj["ps"]?.ToString() ?? singleObj["name"]?.ToString() ?? "";
                    string uri = OutboundJsonToUri(singleObj, docRemarks);
                    if (!string.IsNullOrEmpty(uri)) servers.Add(ServerParser.ParseServer(uri));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"JSON subscription parse warning: {ex.Message}");
        }
        return servers;
    }

    private static ServerInfo? ParseJsonProfileDoc(JObject doc)
    {
        if (doc["outbounds"] is not JArray outbounds) return null;
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
        server.RawJsonConfig = doc.ToString(Formatting.None);
        return server;
    }

    public static string OutboundJsonToUri(JObject ob, string fallbackName)
    {
        string type = (ob["type"]?.ToString() ?? ob["protocol"]?.ToString() ?? "").ToLowerInvariant();
        if (type is "" or "direct" or "block" or "dns" or "freedom" or "blackhole" or "selector" or "urltest")
            return "";

        string? tag = ob["tag"]?.ToString();
        string name = !string.IsNullOrWhiteSpace(fallbackName)
            ? fallbackName
            : (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("proxy", StringComparison.OrdinalIgnoreCase) ? tag : "");
        string frag = string.IsNullOrWhiteSpace(name) ? "" : $"#{Uri.EscapeDataString(name)}";

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
                if (ob["transport"]?["path"] != null) q.Add($"path={Uri.EscapeDataString(ob["transport"]!["path"]!.ToString())}");
                if (ob["transport"]?["service_name"] != null) q.Add($"serviceName={Uri.EscapeDataString(ob["transport"]!["service_name"]!.ToString())}");
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
                return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessObj.ToString(Formatting.None)));
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
                return $"hysteria2://{pw}@{hostPart}{qs}{frag}";
            }
            if (type == "socks")
            {
                string u = ob["username"]?.ToString() ?? "";
                string p = ob["password"]?.ToString() ?? "";
                string auth = !string.IsNullOrEmpty(u) ? $"{Uri.EscapeDataString(u)}:{Uri.EscapeDataString(p)}@" : "";
                return $"socks://{auth}{hostPart}{frag}";
            }
        }

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
                }
                else if (sec == "tls")
                {
                    var ts = ss?["tlsSettings"];
                    q.Add($"sni={Uri.EscapeDataString(ts?["serverName"]?.ToString() ?? server)}");
                    if (ts?["allowInsecure"]?.ToObject<bool>() == true) q.Add("allowInsecure=1");
                }
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
                    ["id"] = uuid,
                    ["aid"] = (user?["alterId"]?.ToObject<int>() ?? 0).ToString(),
                    ["scy"] = user?["security"]?.ToString() ?? "auto",
                    ["net"] = net,
                    ["path"] = ss?["wsSettings"]?["path"]?.ToString() ?? ss?["xhttpSettings"]?["path"]?.ToString() ?? "/",
                    ["host"] = ss?["wsSettings"]?["headers"]?["Host"]?.ToString() ?? "",
                    ["tls"] = sec == "tls" ? "tls" : "",
                    ["sni"] = ss?["tlsSettings"]?["serverName"]?.ToString() ?? ""
                };
                return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessObj.ToString(Formatting.None)));
            }
        }

        if (ob["settings"]?["servers"] is JArray serversArr && serversArr.Count > 0)
        {
            var srv = serversArr[0];
            string server = srv["address"]?.ToString() ?? "";
            int port = srv["port"]?.ToObject<int>() ?? 443;
            string hostPart = server.Contains(':') ? $"[{server}]:{port}" : $"{server}:{port}";
            var ss = ob["streamSettings"];
            string sec = ss?["security"]?.ToString() ?? "tls";

            if (type == "trojan")
            {
                string pw = Uri.EscapeDataString(srv["password"]?.ToString() ?? "");
                string sni = ss?["tlsSettings"]?["serverName"]?.ToString() ?? server;
                return $"trojan://{pw}@{hostPart}?security={sec}&sni={Uri.EscapeDataString(sni)}{frag}";
            }
            if (type == "shadowsocks")
            {
                string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{srv["method"]}:{srv["password"]}"));
                return $"ss://{cred}@{hostPart}{frag}";
            }
        }

        return "";
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

            if (!raw.StartsWith(" ") && !raw.StartsWith("\t") && !trimmed.StartsWith("-") && trimmed.EndsWith(":"))
            {
                FlushCurrent();
                break;
            }

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
            if ((ch == '"' || ch == '\'') && !inQuotes) { inQuotes = true; quoteChar = ch; }
            else if (inQuotes && ch == quoteChar) { inQuotes = false; }
            else if (!inQuotes)
            {
                if (ch is '{' or '[') depth++;
                else if (ch is '}' or ']') depth--;
                else if (ch == ',' && depth == 0) { parts.Add(sb.ToString()); sb.Clear(); continue; }
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());

        foreach (var part in parts)
        {
            int colon = part.IndexOf(':');
            if (colon <= 0) continue;
            dict[part[..colon].Trim().Trim('"', '\'')] = part[(colon + 1)..].Trim().Trim('"', '\'');
        }
        return dict;
    }

    private static string ClashDictToUri(Dictionary<string, string> d)
    {
        if (!d.TryGetValue("type", out string? type) || !d.TryGetValue("server", out string? server))
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
                ["v"] = "2", ["ps"] = name, ["add"] = server, ["port"] = port,
                ["id"] = d.GetValueOrDefault("uuid", ""), ["aid"] = d.GetValueOrDefault("alterId", "0"),
                ["scy"] = d.GetValueOrDefault("cipher", "auto"), ["net"] = d.GetValueOrDefault("network", "tcp"),
                ["path"] = d.GetValueOrDefault("path", "/"),
                ["tls"] = d.GetValueOrDefault("tls", "false").Equals("true", StringComparison.OrdinalIgnoreCase) ? "tls" : "",
                ["sni"] = sni
            };
            return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessObj.ToString(Formatting.None)));
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
        return "";
    }

    private static SubscriptionMetadata ParseHeaders(HttpResponseMessage response)
    {
        var meta = new SubscriptionMetadata();
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

        string supportUrl = GetHeader("support-url");
        if (!string.IsNullOrWhiteSpace(supportUrl))
            meta.SupportUrl = supportUrl.Trim();

        string webPageUrl = GetHeader("profile-web-page-url");
        if (!string.IsNullOrWhiteSpace(webPageUrl))
            meta.WebPageUrl = webPageUrl.Trim();

        meta.LastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return meta;
    }

    private static void ParseBodyMetadata(List<string> lines, SubscriptionMetadata meta)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("#")) continue;
            string comment = line.TrimStart('#', ' ');
            if (comment.StartsWith("profile-title:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(meta.ProfileTitle))
                meta.ProfileTitle = DecodeHeaderValue(comment[14..].Trim());
            else if (comment.StartsWith("support-url:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(meta.SupportUrl))
                meta.SupportUrl = comment[12..].Trim();
            else if (comment.StartsWith("profile-web-page-url:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(meta.WebPageUrl))
                meta.WebPageUrl = comment[21..].Trim();
        }
    }

    private static string DecodeHeaderValue(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string b64 = raw[7..].Trim().Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight((b64.Length + 3) / 4 * 4, '=');
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch { }
        }
        try { return Uri.UnescapeDataString(raw); } catch { return raw; }
    }

    public static async Task<List<ServerInfo>> ParseMassLinksAsync(string text)
    {
        var servers = new List<ServerInfo>();
        if (string.IsNullOrWhiteSpace(text)) return servers;

        var trimmed = text.Trim('\uFEFF', ' ', '\t', '\r', '\n');
        var rawLines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>();
        foreach (var rawLine in rawLines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            bool startsWithProtocol = Config.SupportedProtocols.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            bool hasSecondProtocol = startsWithProtocol && Config.SupportedProtocols.Any(p =>
            {
                int first = line.IndexOf(p, StringComparison.OrdinalIgnoreCase);
                return first >= 0 && line.IndexOf(p, first + p.Length, StringComparison.OrdinalIgnoreCase) >= 0;
            });
            bool isSingleUri = startsWithProtocol && !hasSecondProtocol;
            if (isSingleUri)
                tokens.Add(line);
            else
                tokens.AddRange(Regex.Split(line, @"[\s,;]+"));
        }
        foreach (var token in tokens)
        {
            string l = token.Trim();
            if (string.IsNullOrEmpty(l) || l.StartsWith("#")) continue;

            if (Config.SupportedProtocols.Any(p => l.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                if (ValidateServerUri(l))
                    servers.Add(ServerParser.ParseServer(l));
            }
            else if (l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     l.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var subServers = await LoadSubscription(l);
                    foreach (var s in subServers) s.SubscriptionUrl = l;
                    servers.AddRange(subServers);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to load subscription from {l}: {ex.Message}");
                }
            }
        }

        if (servers.Count == 0)
            servers.AddRange(ParseSubscriptionBody(trimmed, null));

        return ServerParser.DeduplicateNames(servers);
    }

    public static bool ValidateServerUri(string uri)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uri)) return false;
            uri = uri.Trim();
            if (!Config.SupportedProtocols.Any(p => uri.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return false;
            var (host, port) = ServerParser.ExtractHostPort(uri);
            return !string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535;
        }
        catch { return false; }
    }
}
