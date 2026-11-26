using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.Models;

namespace Hivox.Mobile.Services;

public static class GeoIPService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(6) };

    public static Dictionary<string, (string Flag, string Name, string Code)> LoadCache(string appDir)
    {
        string cacheFile = Path.Combine(appDir, "country_cache.json");
        if (File.Exists(cacheFile))
        {
            try
            {
                string json = File.ReadAllText(cacheFile);
                var cache = new Dictionary<string, (string, string, string)>();
                var jObj = JObject.Parse(json);
                foreach (var prop in jObj.Properties())
                {
                    if (prop.Value is JArray arr && arr.Count >= 3)
                        cache[prop.Name] = (arr[0].ToString(), arr[1].ToString(), arr[2].ToString());
                }
                return cache;
            }
            catch { }
        }
        return new Dictionary<string, (string, string, string)>();
    }

    public static void SaveCache(string appDir, Dictionary<string, (string Flag, string Name, string Code)> cache)
    {
        string cacheFile = Path.Combine(appDir, "country_cache.json");
        try
        {
            var jObj = new JObject();
            foreach (var kv in cache)
                jObj[kv.Key] = new JArray(kv.Value.Flag, kv.Value.Name, kv.Value.Code);
            File.WriteAllText(cacheFile, jObj.ToString());
        }
        catch { }
    }

    public static string CodeToFlag(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            return "\U0001F310";
        string upper = countryCode.ToUpperInvariant();
        if (upper[0] < 'A' || upper[0] > 'Z' || upper[1] < 'A' || upper[1] > 'Z')
            return "\U0001F310";
        int cp1 = 0x1F1E6 + (upper[0] - 'A');
        int cp2 = 0x1F1E6 + (upper[1] - 'A');
        return char.ConvertFromUtf32(cp1) + char.ConvertFromUtf32(cp2);
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || b[0] == 127;
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
    }

    public static async Task ResolveServerCountries(List<ServerInfo> servers, string appDir, CancellationToken ct = default)
    {
        var cache = LoadCache(appDir);
        bool dirty = false;

        foreach (var server in servers)
        {
            if (ct.IsCancellationRequested) break;
            if (server.CountryName != "Unknown") continue;
            string host = server.Host.Trim('[', ']');
            if (string.IsNullOrEmpty(host)) continue;

            if (cache.TryGetValue(host, out var cached))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    server.Flag = cached.Flag;
                    server.CountryName = cached.Name;
                    server.CountryCode = cached.Code;
                });
                continue;
            }

            try
            {
                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip!))
                {
                    var addrs = await Dns.GetHostAddressesAsync(host, ct);
                    if (addrs.Length == 0) continue;
                    ip = addrs[0];
                }

                if (IsPrivateOrLoopback(ip))
                    continue;

                // HTTPS endpoint (cleartext HTTP is blocked by Android network_security_config)
                string url = $"https://ipwho.is/{ip}";
                var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(ct);
                    var data = JObject.Parse(json);
                    if (data["success"]?.Value<bool>() == true)
                    {
                        string cc = (data["country_code"]?.ToString() ?? "").ToUpperInvariant();
                        string cnameEn = data["country"]?.ToString() ?? "";
                        if (cc.Length == 2 && !string.IsNullOrEmpty(cnameEn))
                        {
                            string flag = CodeToFlag(cc);
                            string upperName = cnameEn.ToUpperInvariant();
                            cache[host] = (flag, upperName, cc);
                            dirty = true;
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                server.Flag = flag;
                                server.CountryName = upperName;
                                server.CountryCode = cc;
                            });
                        }
                    }
                }
                await Task.Delay(350, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }

        if (dirty)
            SaveCache(appDir, cache);
    }
}
