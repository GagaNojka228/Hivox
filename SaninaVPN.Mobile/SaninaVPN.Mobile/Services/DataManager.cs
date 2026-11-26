using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.Models;

namespace Hivox.Mobile.Services;

public class DataManager
{
    private readonly string _dataFile;
    private const string CurrentVersion = "2.0";
    private static readonly object _fileLock = new();

    private static readonly HashSet<string> SecretSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "proxy_auth_user",
        "proxy_auth_password",
        "proxy_auth_pass",
        "selected_server_uri"
    };

    public DataManager(string dataFile)
    {
        _dataFile = dataFile;
    }

    public void SaveData(Dictionary<string, object> data)
    {
        lock (_fileLock)
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
    }

    public Dictionary<string, object> LoadData()
    {
        lock (_fileLock)
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
                            SaveData(new Dictionary<string, object>(data));
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
    }

    private static List<Dictionary<string, object>> SerializeServers(List<ServerInfo> servers) =>
        servers.ConvertAll(s => new Dictionary<string, object>
        {
            ["uri"] = s.Uri,
            ["name"] = s.Name,
            ["protocol"] = s.Protocol,
            ["transport"] = s.Transport,
            ["security"] = s.Security,
            ["details"] = s.Details,
            ["host"] = s.Host,
            ["ping"] = s.Ping,
            ["country_code"] = s.CountryCode,
            ["country_name"] = s.CountryName,
            ["flag"] = s.Flag,
            ["port"] = s.Port,
            ["subscription_url"] = s.SubscriptionUrl,
            ["raw_json_config"] = s.RawJsonConfig
        });

    private static Dictionary<string, object> SerializeSubMeta(Dictionary<string, SubscriptionMetadata>? subMeta)
    {
        var metaDict = new Dictionary<string, object>();
        if (subMeta == null) return metaDict;

        foreach (var kv in subMeta.ToList())
        {
            metaDict[kv.Key] = new Dictionary<string, object>
            {
                ["profile_title"] = kv.Value.ProfileTitle,
                ["upload_bytes"] = kv.Value.UploadBytes,
                ["download_bytes"] = kv.Value.DownloadBytes,
                ["total_bytes"] = kv.Value.TotalBytes,
                ["expire_unix"] = kv.Value.ExpireUnix,
                ["update_interval_hours"] = kv.Value.UpdateIntervalHours,
                ["last_updated_unix"] = kv.Value.LastUpdatedUnix,
                ["support_url"] = kv.Value.SupportUrl,
                ["web_page_url"] = kv.Value.WebPageUrl,
                ["custom_name"] = kv.Value.CustomName,
                ["show_on_home"] = kv.Value.ShowOnHome,
                ["always_expanded"] = kv.Value.AlwaysExpanded,
                ["use_custom_interval"] = kv.Value.UseCustomInterval,
                ["custom_interval_minutes"] = kv.Value.CustomIntervalMinutes,
                ["custom_dns"] = kv.Value.CustomDns
            };
        }
        return metaDict;
    }

    public static string BuildBackupJson(
        List<string> subscriptions,
        List<ServerInfo> servers,
        List<Dictionary<string, string>> history,
        Dictionary<string, object> settings,
        Dictionary<string, SubscriptionMetadata>? subMeta = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["_version"] = CurrentVersion,
            ["subscriptions"] = subscriptions,
            ["servers"] = SerializeServers(servers),
            ["history"] = history,
            ["settings"] = new Dictionary<string, object>(settings),
            ["subscription_meta"] = SerializeSubMeta(subMeta)
        };
        return JsonConvert.SerializeObject(payload, Formatting.Indented);
    }

    public static void SaveAppData(
        string appDir,
        List<string> subscriptions,
        List<ServerInfo> servers,
        List<Dictionary<string, string>> history,
        Dictionary<string, object> settings,
        Dictionary<string, SubscriptionMetadata>? subMeta = null)
    {
        // 1. Store secrets in Android SecureStorage (EncryptedSharedPreferences / KeyStore)
        SecureVaultService.SetSecret(
            SecureVaultService.KeySubscriptionsJson,
            JsonConvert.SerializeObject(subscriptions));

        if (settings.TryGetValue("proxy_auth_user", out var uObj) && uObj is string u && !string.IsNullOrWhiteSpace(u))
            SecureVaultService.SetSecret(SecureVaultService.KeyProxyAuthUser, u);

        string? passVal = settings.TryGetValue("proxy_auth_password", out var pObj) && pObj is string p && !string.IsNullOrWhiteSpace(p)
            ? p
            : (settings.TryGetValue("proxy_auth_pass", out var p2Obj) && p2Obj is string p2 && !string.IsNullOrWhiteSpace(p2) ? p2 : null);
        if (!string.IsNullOrWhiteSpace(passVal))
            SecureVaultService.SetSecret(SecureVaultService.KeyProxyAuthPassword, passVal);

        // Ensure HWID exists inside SecureStorage
        _ = SecureVaultService.GetOrCreateHwid();

        // 2. Store servers, selected server URI, and subscription metadata in AES-256-GCM vault
        string selectedUri = settings.TryGetValue("selected_server_uri", out var selObj) && selObj is string sel ? sel : "";
        var vaultObj = new Dictionary<string, object>
        {
            ["subscriptions"] = subscriptions,
            ["servers"] = SerializeServers(servers),
            ["subscription_meta"] = SerializeSubMeta(subMeta),
            ["selected_server_uri"] = selectedUri
        };
        bool vaultSaved = SecureVaultService.SaveEncryptedVault(appDir, JsonConvert.SerializeObject(vaultObj));

        // 3. Persist ONLY non-secret UI/core preferences in vpn_data_v2.json
        var publicSettings = new Dictionary<string, object>();
        foreach (var kv in settings)
        {
            if (!SecretSettingKeys.Contains(kv.Key))
                publicSettings[kv.Key] = kv.Value;
        }

        var publicData = new Dictionary<string, object>
        {
            ["vault_protected"] = vaultSaved,
            ["history"] = history,
            ["settings"] = publicSettings
        };

        var dm = new DataManager(Path.Combine(appDir, "vpn_data_v2.json"));
        dm.SaveData(publicData);
    }

    private static Dictionary<string, object> CreateDefaultSettings() => new()
    {
        ["auto_update"] = true,
        ["dark_mode"] = true,
        ["app_theme"] = "dark",
        ["game_mode"] = false,
        ["auto_select_best"] = true,
        ["auto_ping"] = true,
        ["use_tun_mode"] = true,
        ["mux_enabled"] = true,
        ["fragmentation"] = false,
        ["preferred_ip"] = "auto",
        ["http_enabled"] = true,
        ["socks_enabled"] = true,
        ["http_port"] = 10809,
        ["socks_port"] = 10808,
        ["ping_type"] = "tcp",
        ["ping_display"] = "time",
        ["ping_test_url"] = "https://www.gstatic.com/generate_204",
        ["ping_timeout"] = 3,
        ["update_interval"] = 6,
        ["update_notify"] = true,
        ["request_timeout"] = 15,
        ["update_on_open"] = true,
        ["ping_on_open"] = true,
        ["connect_on_open"] = false,
        ["connect_on_boot"] = false,
        ["no_duplicates"] = true,
        ["send_hwid"] = true,
        ["custom_user_agent"] = "v2rayNG/1.9.34",
        ["default_sort"] = "none",
        ["selected_server_uri"] = "",
        ["auto_reconnect"] = true,
        ["bypass_lan"] = true,
        ["bypass_ru"] = true,
        ["allow_lan"] = false,
        ["sniffing_route_only"] = true,
        ["block_udp"] = false,
        ["proxy_auth_enabled"] = false,
        ["http_auth_hotspot"] = true,
        ["custom_dns"] = "1.1.1.1",
        ["tun_mtu"] = 1500,
        ["memory_limit_mb"] = 100,
        ["memory_unlimited"] = false,
        ["keep_wakelock"] = true,
        ["notif_show_speed"] = false,
        ["notif_show_server"] = true,
        ["disconnect_on_lock"] = false,
        ["show_memory_usage"] = false,
        ["ping_on_update"] = true,
        ["fragment_packets"] = "tlshello",
        ["fragment_length"] = "100-200",
        ["fragment_interval"] = "10-20",
        ["noises_enabled"] = false,
        ["noise_type"] = "rand",
        ["noise_packet"] = "50-100",
        ["noise_delay"] = "10-15",
        ["mux_concurrency"] = 8,
        ["mux_xudp_concurrency"] = 16,
        ["mux_xudp_quic"] = "reject",
        ["per_app_proxy_enabled"] = false,
        ["per_app_proxy_mode"] = "bypass",
        ["per_app_packages"] = "",
        ["geo_auto_update"] = true,
        ["geo_update_interval_days"] = 7,
        ["geo_last_updated_unix"] = 0L
    };

    private static void ParseServersArray(JArray? srv, List<ServerInfo> servers)
    {
        if (srv == null) return;
        foreach (var item in srv)
        {
            if (item is not JObject sd) continue;
            string proto = sd["protocol"]?.ToString() ?? "Unknown";
            string tr = sd["transport"]?.ToString() ?? "";
            string sec = sd["security"]?.ToString() ?? "";
            string details = sd["details"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(details))
                details = $"{proto} / {(string.IsNullOrEmpty(tr) ? "TCP" : tr)} / {(string.IsNullOrEmpty(sec) ? "none" : sec)}";

            servers.Add(new ServerInfo
            {
                Uri = sd["uri"]?.ToString() ?? "",
                Name = sd["name"]?.ToString() ?? "Unknown",
                Protocol = proto,
                Transport = tr,
                Security = sec,
                Details = details,
                Host = sd["host"]?.ToString() ?? "",
                Ping = sd["ping"]?.ToObject<int>() ?? -1,
                CountryCode = sd["country_code"]?.ToString() ?? "",
                CountryName = sd["country_name"]?.ToString() ?? "Unknown",
                Flag = sd["flag"]?.ToString() ?? "\U0001F310",
                Port = sd["port"]?.ToObject<int>() ?? 443,
                SubscriptionUrl = sd["subscription_url"]?.ToString() ?? "",
                RawJsonConfig = sd["raw_json_config"]?.ToString() ?? ""
            });
        }
    }

    private static void ParseSubMetaObject(JObject? metaDict, Dictionary<string, SubscriptionMetadata> subMeta)
    {
        if (metaDict == null) return;
        foreach (var kv in metaDict)
        {
            if (kv.Value is not JObject m) continue;
            subMeta[kv.Key] = new SubscriptionMetadata
            {
                ProfileTitle = m["profile_title"]?.ToString() ?? m["ProfileTitle"]?.ToString() ?? "",
                UploadBytes = m["upload_bytes"]?.ToObject<long>() ?? m["UploadBytes"]?.ToObject<long>() ?? 0,
                DownloadBytes = m["download_bytes"]?.ToObject<long>() ?? m["DownloadBytes"]?.ToObject<long>() ?? 0,
                TotalBytes = m["total_bytes"]?.ToObject<long>() ?? m["TotalBytes"]?.ToObject<long>() ?? 0,
                ExpireUnix = m["expire_unix"]?.ToObject<long>() ?? m["ExpireUnix"]?.ToObject<long>() ?? 0,
                UpdateIntervalHours = m["update_interval_hours"]?.ToObject<int>() ?? m["UpdateIntervalHours"]?.ToObject<int>() ?? 0,
                LastUpdatedUnix = m["last_updated_unix"]?.ToObject<long>() ?? 0,
                SupportUrl = m["support_url"]?.ToString() ?? "",
                WebPageUrl = m["web_page_url"]?.ToString() ?? "",
                CustomName = m["custom_name"]?.ToString() ?? "",
                ShowOnHome = m["show_on_home"]?.ToObject<bool>() ?? true,
                AlwaysExpanded = m["always_expanded"]?.ToObject<bool>() ?? false,
                UseCustomInterval = m["use_custom_interval"]?.ToObject<bool>() ?? false,
                CustomIntervalMinutes = m["custom_interval_minutes"]?.ToObject<int>() ?? 120,
                CustomDns = m["custom_dns"]?.ToString() ?? ""
            };
        }
    }

    public static (
        List<string> subscriptions,
        List<ServerInfo> servers,
        List<Dictionary<string, string>> history,
        Dictionary<string, object> settings,
        Dictionary<string, SubscriptionMetadata> subMeta)
        LoadAppData(string appDir)
    {
        var dm = new DataManager(Path.Combine(appDir, "vpn_data_v2.json"));
        var data = dm.LoadData();
        var subscriptions = new List<string>();
        var servers = new List<ServerInfo>();
        var history = new List<Dictionary<string, string>>();
        var subMeta = new Dictionary<string, SubscriptionMetadata>();
        var settings = CreateDefaultSettings();
        bool needsSecretMigration = false;

        // 1. Load subscriptions from SecureStorage first
        string? secSubsJson = SecureVaultService.GetSecret(SecureVaultService.KeySubscriptionsJson);
        if (!string.IsNullOrWhiteSpace(secSubsJson))
        {
            try
            {
                var parsedSubs = JsonConvert.DeserializeObject<List<string>>(secSubsJson);
                if (parsedSubs != null) subscriptions = parsedSubs;
            }
            catch { }
        }

        // 2. Load AES-256-GCM encrypted vault (servers + subMeta + selected_server_uri + backup subs)
        string? vaultJson = SecureVaultService.LoadEncryptedVault(appDir);
        if (!string.IsNullOrWhiteSpace(vaultJson))
        {
            try
            {
                var vault = JObject.Parse(vaultJson);
                if (subscriptions.Count == 0 && vault["subscriptions"] is JArray vSubs)
                    subscriptions = vSubs.ToObject<List<string>>() ?? new List<string>();
                ParseServersArray(vault["servers"] as JArray, servers);
                ParseSubMetaObject(vault["subscription_meta"] as JObject, subMeta);
                string sel = vault["selected_server_uri"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(sel))
                    settings["selected_server_uri"] = sel;
            }
            catch (Exception ex)
            {
                Logger.Error($"Vault parse error: {ex.Message}");
            }
        }

        // 3. Migrate legacy plaintext subscriptions/servers from vpn_data_v2.json if present
        if (data.TryGetValue("subscriptions", out var legacySubsObj) && legacySubsObj is JArray legacySubs && legacySubs.Count > 0)
        {
            needsSecretMigration = true;
            if (subscriptions.Count == 0)
                subscriptions = legacySubs.ToObject<List<string>>() ?? new List<string>();
        }
        if (data.TryGetValue("servers", out var legacySrvObj) && legacySrvObj is JArray legacySrv && legacySrv.Count > 0)
        {
            needsSecretMigration = true;
            if (servers.Count == 0)
                ParseServersArray(legacySrv, servers);
        }
        if (data.TryGetValue("subscription_meta", out var legacyMetaObj) && legacyMetaObj is JObject legacyMeta && legacyMeta.Count > 0)
        {
            needsSecretMigration = true;
            if (subMeta.Count == 0)
                ParseSubMetaObject(legacyMeta, subMeta);
        }

        if (data.TryGetValue("history", out var histObj) && histObj is JArray hist)
            history = hist.ToObject<List<Dictionary<string, string>>>() ?? new List<Dictionary<string, string>>();

        if (data.TryGetValue("settings", out var stObj) && stObj is JObject st)
        {
            foreach (var kv in st)
            {
                if (SecretSettingKeys.Contains(kv.Key))
                    needsSecretMigration = true;

                if (kv.Value is JValue jVal)
                {
                    if (jVal.Type == JTokenType.Boolean)
                        settings[kv.Key] = (bool)jVal;
                    else if (jVal.Type == JTokenType.Integer)
                    {
                        long lv = (long)jVal;
                        settings[kv.Key] = lv is >= int.MinValue and <= int.MaxValue ? (int)lv : lv;
                    }
                    else
                        settings[kv.Key] = jVal.ToString();
                }
            }
            if (settings.TryGetValue("custom_user_agent", out var uaObj) &&
                uaObj is string uaVal &&
                uaVal == "Hivox/2.0 (Android; Xray-core/26.3)")
            {
                settings["custom_user_agent"] = "v2rayNG/1.9.34";
            }
        }

        // 4. Load proxy credentials from SecureStorage (overriding any migrated plaintext)
        string? secUser = SecureVaultService.GetSecret(SecureVaultService.KeyProxyAuthUser);
        if (!string.IsNullOrWhiteSpace(secUser))
            settings["proxy_auth_user"] = secUser;

        string? secPass = SecureVaultService.GetSecret(SecureVaultService.KeyProxyAuthPassword);
        if (!string.IsNullOrWhiteSpace(secPass))
            settings["proxy_auth_password"] = secPass;

        if (needsSecretMigration)
        {
            Logger.Log("Migrating plaintext subscriptions/servers/credentials to SecureStorage vault...");
            SaveAppData(appDir, subscriptions, servers, history, settings, subMeta);
        }

        return (subscriptions, servers, history, settings, subMeta);
    }
}
