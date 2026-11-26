using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Hivox.Models;

namespace Hivox.Services
{
    public class DataManager
    {
        private readonly string _dataFile;
        private const string CurrentVersion = "2.0";

        public static string ResolveDataDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hivox");

            try
            {
                bool hasLocalData = File.Exists(Path.Combine(baseDir, "vpn_data_v2.json"));
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

                bool isInstalledPath =
                    (!string.IsNullOrEmpty(progFiles) && baseDir.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(progFilesX86) && baseDir.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(localPrograms) && baseDir.StartsWith(localPrograms, StringComparison.OrdinalIgnoreCase));

                if (!isInstalledPath || hasLocalData)
                {
                    string probe = Path.Combine(baseDir, $".write_test_{Guid.NewGuid():N}");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    return baseDir;
                }
            }
            catch { }

            Directory.CreateDirectory(roaming);
            return roaming;
        }

        public DataManager(string dataFile)
        {
            _dataFile = dataFile;
        }

        public void SaveData(Dictionary<string, object> data)
        {
            data["_version"] = CurrentVersion;
            try
            {
                string tmpFile = _dataFile + ".tmp";
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(tmpFile, json);
                if (File.Exists(_dataFile))
                    File.Replace(tmpFile, _dataFile, null);
                else
                    File.Move(tmpFile, _dataFile);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save data: {ex.Message}");
            }
        }

        public Dictionary<string, object> LoadData()
        {
            try
            {
                if (File.Exists(_dataFile))
                {
                    string json = File.ReadAllText(_dataFile);
                    var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (data != null)
                    {
                        if (!data.ContainsKey("_version"))
                        {
                            Logger.Log("Migrating config from v1.x to v2.0");
                            data["_version"] = CurrentVersion;
                            var dict = new Dictionary<string, object>(data);
                            SaveData(dict);
                        }
                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load data: {ex.Message}");
            }
            return new Dictionary<string, object>();
        }

        private static Dictionary<string, object> SerializeSubMeta(Dictionary<string, SubscriptionMetadata> subMeta)
        {
            var dict = new Dictionary<string, object>();
            if (subMeta == null) return dict;
            foreach (var kv in subMeta)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                dict[kv.Key] = new Dictionary<string, object>
                {
                    ["profile_title"] = kv.Value.ProfileTitle ?? "",
                    ["upload_bytes"] = kv.Value.UploadBytes,
                    ["download_bytes"] = kv.Value.DownloadBytes,
                    ["total_bytes"] = kv.Value.TotalBytes,
                    ["expire_unix"] = kv.Value.ExpireUnix,
                    ["update_interval_hours"] = kv.Value.UpdateIntervalHours,
                    ["last_updated_unix"] = kv.Value.LastUpdatedUnix,
                    ["support_url"] = kv.Value.SupportUrl ?? "",
                    ["web_page_url"] = kv.Value.WebPageUrl ?? "",
                    ["custom_name"] = kv.Value.CustomName ?? "",
                    ["never_collapse"] = kv.Value.NeverCollapse,
                    ["custom_update_interval_minutes"] = kv.Value.CustomUpdateIntervalMinutes,
                    ["custom_dns"] = kv.Value.CustomDns ?? "",
                    ["custom_sort"] = kv.Value.CustomSort ?? ""
                };
            }
            return dict;
        }

        public static string BuildBackupJson(List<string> subscriptions, List<ServerInfo> servers,
            List<Dictionary<string, string>> history, Dictionary<string, object> settings,
            Dictionary<string, SubscriptionMetadata> subMeta = null)
        {
            var payload = new Dictionary<string, object>
            {
                ["_version"] = CurrentVersion,
                ["subscriptions"] = subscriptions,
                ["servers"] = servers.ConvertAll(s => new Dictionary<string, object>
                {
                    ["uri"] = s.Uri, ["name"] = s.Name, ["protocol"] = s.Protocol,
                    ["transport"] = s.Transport, ["security"] = s.Security, ["host"] = s.Host,
                    ["ping"] = s.Ping, ["country_code"] = s.CountryCode,
                    ["country_name"] = s.CountryName, ["flag"] = s.Flag,
                    ["port"] = s.Port, ["details"] = s.Details,
                    ["raw_json"] = s.RawJsonConfig,
                    ["subscription_url"] = s.SubscriptionUrl,
                    ["subscription_name"] = s.SubscriptionName
                }),
                ["history"] = history,
                ["settings"] = new Dictionary<string, object>(settings),
                ["subscription_meta"] = SerializeSubMeta(subMeta)
            };
            return JsonConvert.SerializeObject(payload, Formatting.Indented);
        }

        public static void SaveAppData(string appDir, List<string> subscriptions, List<ServerInfo> servers,
            List<Dictionary<string, string>> history, Dictionary<string, object> settings,
            Dictionary<string, SubscriptionMetadata> subMeta = null)
        {
            var data = new Dictionary<string, object>
            {
                ["subscriptions"] = subscriptions,
                ["servers"] = servers.ConvertAll(s => new Dictionary<string, object>
                {
                    ["uri"] = s.Uri, ["name"] = s.Name, ["protocol"] = s.Protocol,
                    ["transport"] = s.Transport, ["security"] = s.Security, ["host"] = s.Host,
                    ["ping"] = s.Ping, ["country_code"] = s.CountryCode,
                    ["country_name"] = s.CountryName, ["flag"] = s.Flag,
                    ["port"] = s.Port, ["details"] = s.Details,
                    ["raw_json"] = s.RawJsonConfig,
                    ["subscription_url"] = s.SubscriptionUrl,
                    ["subscription_name"] = s.SubscriptionName
                }),
                ["history"] = history,
                ["settings"] = settings,
                ["subscription_meta"] = SerializeSubMeta(subMeta)
            };
            var dm = new DataManager(Path.Combine(appDir, "vpn_data_v2.json"));
            dm.SaveData(data);
        }

        public static (List<string> subscriptions, List<ServerInfo> servers, List<Dictionary<string, string>> history, Dictionary<string, object> settings, Dictionary<string, SubscriptionMetadata> subMeta)
            LoadAppData(string appDir)
        {
            var dm = new DataManager(Path.Combine(appDir, "vpn_data_v2.json"));
            var data = dm.LoadData();
            var subscriptions = new List<string>();
            var servers = new List<ServerInfo>();
            var history = new List<Dictionary<string, string>>();
            var subMeta = new Dictionary<string, SubscriptionMetadata>(StringComparer.OrdinalIgnoreCase);
            var settings = new Dictionary<string, object>
            {
                ["auto_update"] = true, ["dark_mode"] = true, ["minimize_to_tray"] = true,
                ["auto_start"] = false, ["show_speed"] = true, ["auto_select_best"] = true,
                ["use_tun_mode"] = false, ["use_singbox"] = false, ["mux_enabled"] = true,
                ["connect_on_open"] = false, ["auto_reconnect"] = true,
                ["bypass_lan"] = true, ["bypass_ru"] = true, ["allow_lan"] = false,
                ["disconnect_on_lock"] = false,
                ["custom_dns"] = "1.1.1.1", ["tun_mtu"] = 1420,
                ["fragmentation"] = false, ["fragment_packets"] = "tlshello",
                ["fragment_length"] = "100-200", ["fragment_interval"] = "10-20",
                ["noises_enabled"] = false, ["noise_type"] = "rand",
                ["noise_packet"] = "50-100", ["noise_delay"] = "10-15",
                ["mux_concurrency"] = 8, ["mux_xudp_concurrency"] = 16, ["mux_xudp_quic"] = "reject",
                ["block_udp"] = false, ["sniffing_route_only"] = true,
                ["proxy_auth_enabled"] = false, ["http_auth_hotspot"] = true,
                ["proxy_auth_user"] = "", ["proxy_auth_password"] = "",
                ["geo_auto_update"] = true, ["geo_update_interval_days"] = 7, ["geo_last_updated_unix"] = 0L,
                ["process_rules_enabled"] = false, ["process_rules_mode"] = "bypass", ["process_rules_list"] = ""
            };

            if (data.ContainsKey("subscriptions"))
            {
                var subs = data["subscriptions"] as Newtonsoft.Json.Linq.JArray;
                if (subs != null)
                    subscriptions = subs.ToObject<List<string>>() ?? new List<string>();
            }
            if (data.ContainsKey("servers"))
            {
                var srv = data["servers"] as Newtonsoft.Json.Linq.JArray;
                if (srv != null)
                {
                    foreach (var item in srv)
                    {
                        var sd = item as Newtonsoft.Json.Linq.JObject;
                        if (sd != null)
                        {
                            string uri = sd["uri"]?.ToString() ?? "";
                            string name = sd["name"]?.ToString() ?? "Unknown";
                            string host = sd["host"]?.ToString() ?? "";
                            string cc = sd["country_code"]?.ToString() ?? "";
                            string cname = sd["country_name"]?.ToString() ?? "Unknown";
                            string flag = sd["flag"]?.ToString() ?? "\U0001F310";
                            string security = sd["security"]?.ToString() ?? "";
                            if (string.IsNullOrEmpty(security) && !string.IsNullOrEmpty(uri))
                            {
                                var parsedSec = ServerParser.ExtractSecurity(uri);
                                security = parsedSec == "none" ? "" : parsedSec;
                            }
                            if (string.IsNullOrEmpty(cc) || cname == "Unknown")
                            {
                                var (f, cn, code) = ServerParser.ExtractCountryInfoWithCode(name, host);
                                if (string.IsNullOrEmpty(cc)) cc = code;
                                if (cname == "Unknown") cname = cn;
                                if (flag == "\U0001F310") flag = f;
                            }
                            servers.Add(new ServerInfo
                            {
                                Uri = uri,
                                Name = name,
                                Protocol = sd["protocol"]?.ToString() ?? "Unknown",
                                Transport = sd["transport"]?.ToString() ?? "",
                                Security = security,
                                Host = host,
                                Ping = sd["ping"]?.ToObject<int>() ?? -1,
                                CountryCode = cc,
                                CountryName = cname,
                                Flag = flag,
                                Port = sd["port"]?.ToObject<int>() ?? 443,
                                Details = sd["details"]?.ToString() ?? "",
                                RawJsonConfig = sd["raw_json"]?.ToString() ?? "",
                                SubscriptionUrl = sd["subscription_url"]?.ToString() ?? "",
                                SubscriptionName = sd["subscription_name"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            if (data.ContainsKey("history"))
            {
                var hist = data["history"] as Newtonsoft.Json.Linq.JArray;
                if (hist != null)
                    history = hist.ToObject<List<Dictionary<string, string>>>() ?? new List<Dictionary<string, string>>();
            }
            if (data.ContainsKey("settings"))
            {
                var st = data["settings"] as Newtonsoft.Json.Linq.JObject;
                if (st != null)
                {
                    foreach (var kv in st)
                    {
                        if (kv.Value is Newtonsoft.Json.Linq.JValue jVal)
                        {
                            if (jVal.Type == Newtonsoft.Json.Linq.JTokenType.Boolean)
                                settings[kv.Key] = (bool)jVal;
                            else if (jVal.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                                settings[kv.Key] = (long)jVal is >= int.MinValue and <= int.MaxValue ? (int)jVal : (long)jVal;
                            else
                                settings[kv.Key] = jVal?.ToString() ?? "";
                        }
                    }
                }
            }
            if (data.ContainsKey("subscription_meta"))
            {
                var metaDict = data["subscription_meta"] as Newtonsoft.Json.Linq.JObject;
                if (metaDict != null)
                {
                    foreach (var kv in metaDict)
                    {
                        if (kv.Value is not Newtonsoft.Json.Linq.JObject m) continue;
                        subMeta[kv.Key] = new SubscriptionMetadata
                        {
                            ProfileTitle = m["profile_title"]?.ToString() ?? "",
                            UploadBytes = m["upload_bytes"]?.ToObject<long>() ?? 0,
                            DownloadBytes = m["download_bytes"]?.ToObject<long>() ?? 0,
                            TotalBytes = m["total_bytes"]?.ToObject<long>() ?? 0,
                            ExpireUnix = m["expire_unix"]?.ToObject<long>() ?? 0,
                            UpdateIntervalHours = m["update_interval_hours"]?.ToObject<int>() ?? 0,
                            LastUpdatedUnix = m["last_updated_unix"]?.ToObject<long>() ?? 0,
                            SupportUrl = m["support_url"]?.ToString() ?? "",
                            WebPageUrl = m["web_page_url"]?.ToString() ?? "",
                            CustomName = m["custom_name"]?.ToString() ?? "",
                            NeverCollapse = m["never_collapse"]?.ToObject<bool>() ?? false,
                            CustomUpdateIntervalMinutes = m["custom_update_interval_minutes"]?.ToObject<int>() ?? 0,
                            CustomDns = m["custom_dns"]?.ToString() ?? "",
                            CustomSort = m["custom_sort"]?.ToString() ?? ""
                        };
                    }
                }
            }
            return (subscriptions, servers, history, settings, subMeta);
        }
    }
}
