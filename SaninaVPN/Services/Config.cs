using System.Collections.Generic;

namespace Hivox.Services
{
    public static class Config
    {
        public static int ProxyPort { get; set; } = 10809;
        public static int SocksPort { get; set; } = 10808;
        public static int TunPort { get; set; } = 2080;
        public static int PingInterval { get; set; } = 45;
        public const int PingTimeout = 2;
        public const int PingParallel = 20;
        public const int PingMultiCount = 2;
        public const int MaxHistoryEntries = 50;
        public static int RequestTimeout { get; set; } = 15;
        public static bool AutoReconnect { get; set; } = true;
        public const int ReconnectDelay = 3;
        public const int HealthCheckInterval = 3;
        public const int ReconnectMaxAttempts = 5;
        public static int TunMtu { get; set; } = 1420;
        public const string TunInet4Address = "172.19.0.1/30";
        public const string TunInet6Address = "fdfe:dcba:9876::1/126";
        public const string WindowSize = "1100x720";
        public const int SpeedHistorySize = 60;
        public const int MaxPingThreshold = 300;
        public const int TcpKeepaliveInterval = 30;
        public const int ConnectionTimeout = 3;

        // Runtime Tunnel, Routing & Connection Settings
        public static string CustomDns { get; set; } = "1.1.1.1";
        public static bool BypassLan { get; set; } = true;
        public static bool BypassRu { get; set; } = true;
        public static bool AllowLan { get; set; } = false;
        public static bool SniffingRouteOnly { get; set; } = true;
        public static bool BlockUdp { get; set; } = false;
        public static bool DisconnectOnLock { get; set; } = false;

        public static bool ProxyAuthEnabled { get; set; } = false;
        public static bool HttpAuthOnHotspot { get; set; } = true;
        public static bool HttpEnabled { get; set; } = true;
        public static bool SocksEnabled { get; set; } = true;
        public static string ProxyAuthUser { get; set; } = "";
        public static string ProxyAuthPassword { get; set; } = "";

        public static string FragmentPackets { get; set; } = "tlshello";
        public static string FragmentLength { get; set; } = "100-200";
        public static string FragmentInterval { get; set; } = "10-20";

        public static bool NoisesEnabled { get; set; } = false;
        public static string NoiseType { get; set; } = "rand";
        public static string NoisePacket { get; set; } = "50-100";
        public static string NoiseDelay { get; set; } = "10-15";

        public static int MuxConcurrency { get; set; } = 8;
        public static int MuxXudpConcurrency { get; set; } = 16;
        public static string MuxXudpQuic { get; set; } = "reject";

        public static bool ProcessRulesEnabled { get; set; } = false;
        public static string ProcessRulesMode { get; set; } = "bypass";
        public static List<string> ProcessRulesList { get; set; } = new();

        public static System.Net.WebProxy CreateLocalHttpProxy()
        {
            var proxy = new System.Net.WebProxy($"http://127.0.0.1:{ProxyPort}");
            bool exposeHttpToLan = HttpEnabled || !SocksEnabled;
            if (ProxyAuthEnabled && HttpAuthOnHotspot && AllowLan && exposeHttpToLan &&
                !string.IsNullOrWhiteSpace(ProxyAuthUser) &&
                !string.IsNullOrWhiteSpace(ProxyAuthPassword))
            {
                proxy.Credentials = new System.Net.NetworkCredential(ProxyAuthUser, ProxyAuthPassword);
            }
            return proxy;
        }

        public static readonly List<string> SupportedProtocols = new()
        {
            "vmess://", "vless://", "trojan://", "ss://",
            "hysteria://", "hysteria2://", "hy2://", "tuic://",
            "socks://", "socks5://", "wireguard://", "wg://", "anytls://", "ssh://"
        };

        public static readonly List<string> RuDomains = new()
        {
            "domain:vk.com", "domain:vk.me", "domain:vkuservideo.net", "domain:vkuseraudio.net",
            "domain:userapi.com", "domain:vk-cdn.net", "domain:ok.ru", "domain:odnoklassniki.ru",
            "domain:yandex.ru", "domain:yandex.com", "domain:yandex.net", "domain:ya.ru",
            "domain:yastatic.net", "domain:yandex.st", "domain:mail.ru", "domain:imgsmail.ru",
            "domain:sberbank.ru", "domain:sber.ru", "domain:tinkoff.ru", "domain:tbank.ru", "domain:vtb.ru",
            "domain:alfabank.ru", "domain:gosuslugi.ru", "domain:mos.ru", "domain:nalog.ru",
            "domain:avito.ru", "domain:ozon.ru", "domain:wildberries.ru", "domain:wb.ru",
            "domain:dns-shop.ru", "domain:mvideo.ru", "domain:eldorado.ru", "domain:citilink.ru",
            "domain:kinopoisk.ru", "domain:ivi.ru", "domain:rutube.ru", "domain:dzen.ru",
            "domain:2gis.ru", "domain:hh.ru", "domain:auto.ru", "domain:cian.ru",
            "domain:pikabu.ru", "domain:habr.com", "domain:sports.ru", "domain:rbc.ru"
        };
    }
}
