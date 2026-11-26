using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Hivox.Models;

namespace Hivox.Services
{
    public static class GeoIPService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

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
            if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length < 2)
                return "\U0001F310";
            string upper = countryCode.ToUpperInvariant();
            if (upper[0] < 'A' || upper[0] > 'Z' || upper[1] < 'A' || upper[1] > 'Z')
                return "\U0001F310";
            int cp1 = 0x1F1E6 + (upper[0] - 'A');
            int cp2 = 0x1F1E6 + (upper[1] - 'A');
            return char.ConvertFromUtf32(cp1) + char.ConvertFromUtf32(cp2);
        }

        private static bool IsPrivateIp(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) return false;
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   bytes[0] == 127;
        }

        public static async Task<bool> ResolveServerCountries(List<ServerInfo> servers, string appDir)
        {
            var cache = LoadCache(appDir);
            bool dirty = false;

            foreach (var server in servers)
            {
                if (!string.IsNullOrWhiteSpace(server.CountryCode) && server.CountryName != "Unknown")
                    continue;
                string host = server.Host;
                if (string.IsNullOrWhiteSpace(host)) continue;

                if (cache.TryGetValue(host, out var cached))
                {
                    server.Flag = cached.Flag;
                    server.CountryName = cached.Name;
                    server.CountryCode = cached.Code.ToUpperInvariant();
                    dirty = true;
                    continue;
                }

                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(host);
                    if (addresses.Length == 0) continue;
                    var ip = addresses[0];
                    if (IsPrivateIp(ip)) continue;

                    string cc = "", cnameEn = "";
                    try
                    {
                        var resp = await _httpClient.GetAsync($"http://ip-api.com/json/{ip}?fields=status,country,countryCode");
                        if (resp.IsSuccessStatusCode)
                        {
                            var data = JObject.Parse(await resp.Content.ReadAsStringAsync());
                            if (data["status"]?.ToString() == "success")
                            {
                                cc = (data["countryCode"]?.ToString() ?? "").Trim().ToUpperInvariant();
                                cnameEn = (data["country"]?.ToString() ?? "").Trim().ToUpperInvariant();
                            }
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(cc))
                    {
                        try
                        {
                            var resp = await _httpClient.GetAsync($"https://ipwho.is/{ip}");
                            if (resp.IsSuccessStatusCode)
                            {
                                var data = JObject.Parse(await resp.Content.ReadAsStringAsync());
                                if (data["success"]?.ToObject<bool>() == true)
                                {
                                    cc = (data["country_code"]?.ToString() ?? "").Trim().ToUpperInvariant();
                                    cnameEn = (data["country"]?.ToString() ?? "").Trim().ToUpperInvariant();
                                }
                            }
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(cc) && !string.IsNullOrEmpty(cnameEn))
                    {
                        string flag = CodeToFlag(cc);
                        cache[host] = (flag, cnameEn, cc);
                        server.Flag = flag;
                        server.CountryName = cnameEn;
                        server.CountryCode = cc;
                        dirty = true;
                    }
                    await Task.Delay(350);
                }
                catch { }
            }

            if (dirty)
                SaveCache(appDir, cache);
            return dirty;
        }
    }
}
