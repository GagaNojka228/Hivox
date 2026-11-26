using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.Models;

namespace Hivox.Mobile.Services;

public static class ServerParser
{
    private static readonly Dictionary<string, (string Flag, string Name)> CountryNames = new()
    {
        ["ru"] = ("\U0001F1F7\U0001F1FA", "RUSSIA"), ["us"] = ("\U0001F1FA\U0001F1F8", "USA"),
        ["de"] = ("\U0001F1E9\U0001F1EA", "GERMANY"), ["nl"] = ("\U0001F1F3\U0001F1F1", "NETHERLANDS"),
        ["fi"] = ("\U0001F1EB\U0001F1EE", "FINLAND"), ["fr"] = ("\U0001F1EB\U0001F1F7", "FRANCE"),
        ["gb"] = ("\U0001F1EC\U0001F1E7", "UK"), ["ee"] = ("\U0001F1EA\U0001F1EA", "ESTONIA"),
        ["pl"] = ("\U0001F1F5\U0001F1F1", "POLAND"), ["lv"] = ("\U0001F1F1\U0001F1FB", "LATVIA"),
        ["jp"] = ("\U0001F1EF\U0001F1F5", "JAPAN"), ["sg"] = ("\U0001F1F8\U0001F1EC", "SINGAPORE"),
        ["hk"] = ("\U0001F1ED\U0001F1F0", "HONG KONG"), ["kr"] = ("\U0001F1F0\U0001F1F7", "SOUTH KOREA"),
        ["ca"] = ("\U0001F1E8\U0001F1E6", "CANADA"), ["au"] = ("\U0001F1E6\U0001F1FA", "AUSTRALIA"),
        ["it"] = ("\U0001F1EE\U0001F1F9", "ITALY"), ["es"] = ("\U0001F1EA\U0001F1F8", "SPAIN"),
        ["no"] = ("\U0001F1F3\U0001F1F4", "NORWAY"), ["cz"] = ("\U0001F1E8\U0001F1FF", "CZECHIA"),
        ["ch"] = ("\U0001F1E8\U0001F1ED", "SWITZERLAND"), ["at"] = ("\U0001F1E6\U0001F1F9", "AUSTRIA"),
        ["be"] = ("\U0001F1E7\U0001F1EA", "BELGIUM"), ["dk"] = ("\U0001F1E9\U0001F1F0", "DENMARK"),
        ["se"] = ("\U0001F1F8\U0001F1EA", "SWEDEN"), ["in"] = ("\U0001F1EE\U0001F1F3", "INDIA"),
        ["br"] = ("\U0001F1E7\U0001F1F7", "BRAZIL"), ["tr"] = ("\U0001F1F9\U0001F1F7", "TURKEY"),
        ["ua"] = ("\U0001F1FA\U0001F1E6", "UKRAINE"), ["ro"] = ("\U0001F1F7\U0001F1F4", "ROMANIA"),
        ["bg"] = ("\U0001F1E7\U0001F1EC", "BULGARIA"), ["pt"] = ("\U0001F1F5\U0001F1F9", "PORTUGAL"),
        ["gr"] = ("\U0001F1EC\U0001F1F7", "GREECE"), ["hu"] = ("\U0001F1ED\U0001F1FA", "HUNGARY"),
        ["ie"] = ("\U0001F1EE\U0001F1EA", "IRELAND"), ["sk"] = ("\U0001F1F8\U0001F1F0", "SLOVAKIA"),
        ["si"] = ("\U0001F1F8\U0001F1EE", "SLOVENIA"), ["hr"] = ("\U0001F1ED\U0001F1F7", "CROATIA"),
        ["rs"] = ("\U0001F1F7\U0001F1F8", "SERBIA"), ["lt"] = ("\U0001F1F1\U0001F1F9", "LITHUANIA"),
        ["my"] = ("\U0001F1F2\U0001F1FE", "MALAYSIA"), ["id"] = ("\U0001F1EE\U0001F1E9", "INDONESIA"),
        ["th"] = ("\U0001F1F9\U0001F1ED", "THAILAND"), ["tw"] = ("\U0001F1F9\U0001F1FC", "TAIWAN"),
        ["vn"] = ("\U0001F1FB\U0001F1F3", "VIETNAM"), ["ae"] = ("\U0001F1E6\U0001F1EA", "UAE"),
        ["il"] = ("\U0001F1EE\U0001F1F1", "ISRAEL"), ["za"] = ("\U0001F1FF\U0001F1E6", "SOUTH AFRICA"),
        ["mx"] = ("\U0001F1F2\U0001F1FD", "MEXICO"), ["ar"] = ("\U0001F1E6\U0001F1F7", "ARGENTINA"),
        ["kz"] = ("\U0001F1F0\U0001F1FF", "KAZAKHSTAN"), ["am"] = ("\U0001F1E6\U0001F1F2", "ARMENIA"),
        ["ge"] = ("\U0001F1EC\U0001F1EA", "GEORGIA"), ["by"] = ("\U0001F1E7\U0001F1FE", "BELARUS"),
        ["md"] = ("\U0001F1F2\U0001F1E9", "MOLDOVA"), ["cy"] = ("\U0001F1E8\U0001F1FE", "CYPRUS"),
        ["is"] = ("\U0001F1EE\U0001F1F8", "ICELAND"), ["lu"] = ("\U0001F1F1\U0001F1FA", "LUXEMBOURG"),
        ["uz"] = ("\U0001F1FA\U0001F1FF", "UZBEKISTAN"), ["az"] = ("\U0001F1E6\U0001F1FF", "AZERBAIJAN"),
        ["eu"] = ("\U0001F1EA\U0001F1FA", "EUROPE")
    };

    private static string SafeUnescape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try { return Uri.UnescapeDataString(text); }
        catch
        {
            try { return HttpUtility.UrlDecode(text); }
            catch { return text; }
        }
    }

    private static JObject? TryParseVmessJson(string uri)
    {
        try
        {
            int hashIdx = uri.IndexOf('#');
            string raw = (hashIdx >= 0 ? uri.Substring(8, hashIdx - 8) : uri.Substring(8)).Trim();
            raw = raw.Replace('-', '+').Replace('_', '/');
            int pad = (4 - raw.Length % 4) % 4;
            if (pad > 0) raw += new string('=', pad);
            string jsonStr = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            return JObject.Parse(jsonStr);
        }
        catch { return null; }
    }

    public static ServerInfo ParseServer(string uri)
    {
        try
        {
            string rawFragment = GetRawRemark(uri);
            string protocol = ExtractProtocol(uri);
            string transport = ExtractTransport(uri);
            string security = ExtractSecurity(uri);
            var (host, port) = ExtractHostPort(uri);
            var (flag, country, code) = ExtractCountryInfoWithCode(rawFragment, host);
            string name = ExtractName(rawFragment, country, host, protocol, transport);
            string details = $"{protocol} / {(string.IsNullOrEmpty(transport) ? "TCP" : transport)} / {security}";

            return new ServerInfo
            {
                Uri = uri,
                Name = name,
                Protocol = protocol,
                Transport = transport,
                Security = security == "none" ? "" : security,
                Host = host,
                Port = port,
                Flag = flag,
                CountryName = country,
                CountryCode = code,
                Details = details
            };
        }
        catch
        {
            return new ServerInfo
            {
                Uri = uri,
                Name = "Unknown Server",
                Protocol = ExtractProtocol(uri),
                Host = "",
                Port = 443,
                Flag = "\U0001F310",
                CountryName = "Unknown",
                CountryCode = "",
                Details = ExtractProtocol(uri)
            };
        }
    }

    private static string GetRawRemark(string uri)
    {
        if (uri.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
        {
            var data = TryParseVmessJson(uri);
            if (data != null)
            {
                foreach (var field in new[] { "ps", "remark", "name", "remarks" })
                {
                    string? val = data[field]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return SafeUnescape(val).Trim();
                }
            }
        }
        int hashIdx = uri.IndexOf('#');
        if (hashIdx >= 0 && hashIdx < uri.Length - 1)
            return SafeUnescape(uri.Substring(hashIdx + 1)).Trim();
        return "";
    }

    public static string ExtractProtocol(string uri)
    {
        var map = new (string Prefix, string Name)[]
        {
            ("vmess://", "VMess"),
            ("vless://", "VLESS"),
            ("trojan://", "Trojan"),
            ("ss://", "SS"),
            ("hysteria2://", "Hysteria2"),
            ("hy2://", "Hysteria2"),
            ("hysteria://", "Hysteria"),
            ("tuic://", "TUIC"),
            ("socks5://", "SOCKS"),
            ("socks://", "SOCKS")
        };
        foreach (var (prefix, name) in map)
            if (uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return name;
        return "Unknown";
    }

    public static string ExtractTransport(string uri)
    {
        try
        {
            string net = "";
            if (uri.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
            {
                var data = TryParseVmessJson(uri);
                if (data != null)
                    net = (data["net"]?.ToString() ?? "").Trim().ToLowerInvariant();
            }
            else
            {
                int hashIdx = uri.IndexOf('#');
                string noFrag = hashIdx >= 0 ? uri.Substring(0, hashIdx) : uri;
                int qIdx = noFrag.IndexOf('?');
                if (qIdx >= 0)
                {
                    string query = noFrag.Substring(qIdx + 1);
                    foreach (string p in query.Split('&'))
                    {
                        var parts = p.Split('=', 2);
                        if (parts.Length == 2 && parts[0].Equals("type", StringComparison.OrdinalIgnoreCase))
                        {
                            net = SafeUnescape(parts[1]).Trim().ToLowerInvariant();
                            break;
                        }
                    }
                }
            }
            var tMap = new Dictionary<string, string>
            {
                ["ws"] = "WS", ["grpc"] = "GRPC", ["h2"] = "HTTP", ["http"] = "HTTP",
                ["xhttp"] = "XHTTP", ["splithttp"] = "XHTTP", ["tcp"] = "", ["raw"] = "",
                ["kcp"] = "KCP", ["quic"] = "QUIC"
            };
            return tMap.TryGetValue(net, out var mapped) ? mapped : net.ToUpperInvariant();
        }
        catch { return ""; }
    }

    public static string ExtractSecurity(string uri)
    {
        if (uri.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
        {
            var data = TryParseVmessJson(uri);
            if (data != null)
            {
                string tls = data["tls"]?.ToString() ?? "";
                if (tls.Equals("tls", StringComparison.OrdinalIgnoreCase) || tls == "true" || tls == "1")
                    return "TLS";
            }
            return "none";
        }

        int hashIdx = uri.IndexOf('#');
        string noFrag = hashIdx >= 0 ? uri.Substring(0, hashIdx) : uri;
        int qIdx = noFrag.IndexOf('?');
        if (qIdx >= 0)
        {
            string query = noFrag.Substring(qIdx + 1);
            foreach (string p in query.Split('&'))
            {
                var parts = p.Split('=', 2);
                if (parts.Length == 2 && parts[0].Equals("security", StringComparison.OrdinalIgnoreCase))
                {
                    var sec = SafeUnescape(parts[1]).Trim().ToUpperInvariant();
                    return string.IsNullOrEmpty(sec) ? "none" : sec;
                }
            }
        }
        if (uri.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase))
            return "TLS";
        return "none";
    }

    private static string ExtractName(string rawRemark, string country, string host, string protocol, string transport)
    {
        if (!string.IsNullOrWhiteSpace(rawRemark))
        {
            string clean = Regex.Replace(rawRemark, @"\uD83C[\uDDE6-\uDDFF]\uD83C[\uDDE6-\uDDFF]\uFE0F?", "");
            clean = Regex.Replace(clean, @"[\u200B-\u200D\uFEFF]", "");
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(clean))
                return clean.Length <= 48 ? clean : clean[..48];

            if (country != "Unknown")
                return country;
            return rawRemark.Trim();
        }

        if (country != "Unknown")
            return string.IsNullOrEmpty(transport) ? country : $"{country} {transport}";

        if (!string.IsNullOrWhiteSpace(host))
            return host;

        return $"{protocol} Server";
    }

    public static (string Host, int Port) ExtractHostPort(string uri)
    {
        try
        {
            int hashIdx = uri.IndexOf('#');
            string noFrag = hashIdx >= 0 ? uri.Substring(0, hashIdx) : uri;

            if (noFrag.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
            {
                var data = TryParseVmessJson(noFrag);
                if (data != null)
                {
                    string add = data["add"]?.ToString() ?? "";
                    int port = int.TryParse(data["port"]?.ToString(), out int p) ? p : 443;
                    return (add, port);
                }
            }
            else if (noFrag.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) && !noFrag.Contains('@'))
            {
                string raw = noFrag.Substring(5).Split('?')[0].Replace('-', '+').Replace('_', '/').Trim();
                int pad = (4 - raw.Length % 4) % 4;
                if (pad > 0) raw += new string('=', pad);
                string full = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                int atIdx = full.LastIndexOf('@');
                if (atIdx >= 0)
                    return SplitHostPort(full.Substring(atIdx + 1));
            }
            else if (noFrag.Contains('@'))
            {
                int atIdx = noFrag.LastIndexOf('@');
                string afterAt = noFrag.Substring(atIdx + 1).Split('?')[0].Split('/')[0];
                return SplitHostPort(afterAt);
            }
        }
        catch { }
        return ("", 443);
    }

    private static (string Host, int Port) SplitHostPort(string hp)
    {
        hp = hp.Trim();
        if (hp.StartsWith("["))
        {
            int close = hp.IndexOf(']');
            if (close > 1)
            {
                string host = hp.Substring(1, close - 1);
                if (close + 2 < hp.Length && hp[close + 1] == ':' && int.TryParse(hp.Substring(close + 2), out int p))
                    return (host, p);
                return (host, 443);
            }
        }
        int lastColon = hp.LastIndexOf(':');
        if (lastColon > 0 && int.TryParse(hp.Substring(lastColon + 1), out int port))
            return (hp.Substring(0, lastColon), port);
        return (hp, 443);
    }

    public static (string Flag, string CountryName) ExtractCountryInfo(string name, string host = "")
    {
        var (flag, country, _) = ExtractCountryInfoWithCode(name, host);
        return (flag, country);
    }

    public static (string Flag, string CountryName, string CountryCode) ExtractCountryInfoWithCode(string name, string host = "")
    {
        if (!string.IsNullOrEmpty(name))
        {
            var flagMatch = Regex.Match(name, @"\uD83C([\uDDE6-\uDDFF])\uD83C([\uDDE6-\uDDFF])");
            if (flagMatch.Success)
            {
                char c1 = (char)('a' + (flagMatch.Groups[1].Value[0] - '\uDDE6'));
                char c2 = (char)('a' + (flagMatch.Groups[2].Value[0] - '\uDDE6'));
                string iso = $"{c1}{c2}";
                if (CountryNames.TryGetValue(iso, out var info))
                    return (info.Flag, info.Name, iso.ToUpperInvariant());
                return (flagMatch.Value, iso.ToUpperInvariant(), iso.ToUpperInvariant());
            }
        }

        string searchText = $"{name} {host}".ToLowerInvariant();
        var countryPatterns = new Dictionary<string, string>
        {
            ["russia"] = "ru", ["россия"] = "ru", ["moscow"] = "ru", ["москва"] = "ru", ["spb"] = "ru",
            ["united states"] = "us", ["usa"] = "us", ["america"] = "us", ["сша"] = "us", ["new york"] = "us", ["los angeles"] = "us", ["dallas"] = "us", ["ashburn"] = "us",
            ["germany"] = "de", ["германия"] = "de", ["frankfurt"] = "de", ["berlin"] = "de", ["falkenstein"] = "de", ["nuremberg"] = "de",
            ["netherlands"] = "nl", ["нидерланды"] = "nl", ["голландия"] = "nl", ["amsterdam"] = "nl", ["амстердам"] = "nl",
            ["finland"] = "fi", ["финляндия"] = "fi", ["helsinki"] = "fi", ["хельсинки"] = "fi",
            ["estonia"] = "ee", ["эстония"] = "ee", ["tallinn"] = "ee",
            ["poland"] = "pl", ["польша"] = "pl", ["warsaw"] = "pl",
            ["latvia"] = "lv", ["латвия"] = "lv", ["riga"] = "lv",
            ["france"] = "fr", ["франция"] = "fr", ["paris"] = "fr",
            ["united kingdom"] = "gb", ["britain"] = "gb", ["london"] = "gb", ["england"] = "gb", ["великобритания"] = "gb",
            ["japan"] = "jp", ["япония"] = "jp", ["tokyo"] = "jp",
            ["singapore"] = "sg", ["сингапур"] = "sg",
            ["hong kong"] = "hk", ["hongkong"] = "hk", ["гонконг"] = "hk",
            ["korea"] = "kr", ["seoul"] = "kr", ["корея"] = "kr",
            ["sweden"] = "se", ["швеция"] = "se", ["stockholm"] = "se",
            ["switzerland"] = "ch", ["швейцария"] = "ch", ["zurich"] = "ch",
            ["austria"] = "at", ["австрия"] = "at", ["vienna"] = "at",
            ["canada"] = "ca", ["канада"] = "ca", ["toronto"] = "ca",
            ["australia"] = "au", ["австралия"] = "au", ["sydney"] = "au",
            ["italy"] = "it", ["италия"] = "it", ["milan"] = "it",
            ["spain"] = "es", ["испания"] = "es", ["madrid"] = "es",
            ["india"] = "in", ["индия"] = "in",
            ["brazil"] = "br", ["бразилия"] = "br",
            ["turkey"] = "tr", ["turkiye"] = "tr", ["турция"] = "tr", ["istanbul"] = "tr",
            ["ukraine"] = "ua", ["украина"] = "ua", ["kiev"] = "ua", ["kyiv"] = "ua",
            ["kazakhstan"] = "kz", ["казахстан"] = "kz", ["almaty"] = "kz", ["astana"] = "kz",
            ["armenia"] = "am", ["армения"] = "am", ["yerevan"] = "am",
            ["georgia"] = "ge", ["грузия"] = "ge", ["tbilisi"] = "ge",
            ["uae"] = "ae", ["оаэ"] = "ae", ["dubai"] = "ae",
            ["belarus"] = "by", ["беларусь"] = "by", ["minsk"] = "by",
            ["romania"] = "ro", ["bulgaria"] = "bg", ["portugal"] = "pt", ["greece"] = "gr",
            ["hungary"] = "hu", ["ireland"] = "ie", ["czech"] = "cz", ["чехия"] = "cz", ["prague"] = "cz",
            ["norway"] = "no", ["норвегия"] = "no", ["denmark"] = "dk", ["belgium"] = "be",
            ["slovakia"] = "sk", ["slovenia"] = "si", ["croatia"] = "hr", ["serbia"] = "rs",
            ["lithuania"] = "lt", ["литва"] = "lt", ["vilnius"] = "lt", ["israel"] = "il",
            ["cyprus"] = "cy", ["moldova"] = "md", ["iceland"] = "is", ["luxembourg"] = "lu",
            ["uzbekistan"] = "uz", ["azerbaijan"] = "az", ["vietnam"] = "vn", ["thailand"] = "th",
            ["taiwan"] = "tw", ["indonesia"] = "id", ["malaysia"] = "my", ["mexico"] = "mx",
            ["argentina"] = "ar", ["south africa"] = "za"
        };
        foreach (var kv in countryPatterns)
        {
            bool matched = kv.Key.Length <= 3 && kv.Key.All(c => c is >= 'a' and <= 'z')
                ? Regex.IsMatch(searchText, $@"(?<![a-z]){Regex.Escape(kv.Key)}(?![a-z])")
                : searchText.Contains(kv.Key);
            if (matched && CountryNames.TryGetValue(kv.Value, out var res))
                return (res.Flag, res.Name, kv.Value.ToUpperInvariant());
        }

        if (!string.IsNullOrEmpty(name))
        {
            var tokenMatches = Regex.Matches(name, @"(?<![A-Za-z])([A-Z]{2})(?![A-Za-z])");
            foreach (Match m in tokenMatches)
            {
                string tok = m.Groups[1].Value.ToLowerInvariant();
                if (tok == "uk") tok = "gb";
                if (CountryNames.TryGetValue(tok, out var res))
                    return (res.Flag, res.Name, tok.ToUpperInvariant());
            }
        }

        if (string.IsNullOrEmpty(host)) return ("\U0001F310", "Unknown", "");
        var hostParts = host.ToLowerInvariant().Split('.');
        string tld = hostParts.Length > 0 ? hostParts[^1] : "";
        if (tld == "uk") tld = "gb";
        if (CountryNames.TryGetValue(tld, out var tldRes))
            return (tldRes.Flag, tldRes.Name, tld.ToUpperInvariant());

        return ("\U0001F310", "Unknown", "");
    }

    public static List<ServerInfo> DeduplicateNames(List<ServerInfo> servers)
    {
        var counts = new Dictionary<string, int>();
        foreach (var s in servers)
            counts[s.Name] = counts.GetValueOrDefault(s.Name, 0) + 1;
        var seen = new Dictionary<string, int>();
        foreach (var s in servers)
        {
            if (counts[s.Name] > 1)
            {
                seen[s.Name] = seen.GetValueOrDefault(s.Name, 0) + 1;
                s.Name = $"{s.Name} #{seen[s.Name]}";
            }
        }
        return servers;
    }
}
