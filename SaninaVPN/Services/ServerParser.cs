using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json.Linq;
using Hivox.Models;

namespace Hivox.Services
{
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
            ["eu"] = ("\U0001F1EA\U0001F1FA", "EUROPE"), ["fm"] = ("\U0001F1EB\U0001F1F2", "MICRONESIA")
        };

        private static string SafeUnescape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try { return Uri.UnescapeDataString(text); }
            catch
            {
                try { return HttpUtility.UrlDecode(text); }
                catch { return text; }
            }
        }

        private static JObject TryParseVmessJson(string uri)
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
                var (flag, country, code) = ExtractCountryInfoWithCode($"{rawFragment}", host);
                string name = ExtractName(uri, rawFragment, country, code, host, protocol, transport);
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
                    CountryCode = ""
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
                        string val = data[field]?.ToString();
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
                ("socks://", "SOCKS"),
                ("wireguard://", "WireGuard"),
                ("wg://", "WireGuard"),
                ("anytls://", "AnyTLS"),
                ("ssh://", "SSH")
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
                uri.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
                return "TLS";
            return "none";
        }

        private static string ExtractName(string uri, string rawRemark, string country, string code, string host, string protocol, string transport)
        {
            if (!string.IsNullOrWhiteSpace(rawRemark))
            {
                // Strip only regional-indicator flag glyphs (which WPF renders as raw "DE"/"RU" letters)
                // and invisible zero-width chars, while displaying the crisp graphical flag separately.
                string withoutFlagGlyphs = Regex.Replace(rawRemark, @"\uD83C[\uDDE6-\uDDFF]\uD83C[\uDDE6-\uDDFF]\uFE0F?", "");
                withoutFlagGlyphs = Regex.Replace(withoutFlagGlyphs, @"[\u200B-\u200D\uFEFF]", "");
                withoutFlagGlyphs = Regex.Replace(withoutFlagGlyphs, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(withoutFlagGlyphs))
                    return withoutFlagGlyphs;

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
                    int atIdx = noFrag.IndexOf('@');
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
                ["russia"] = "ru", ["россия"] = "ru", ["moscow"] = "ru", ["москва"] = "ru", ["spb"] = "ru", ["санкт-петербург"] = "ru", ["питер"] = "ru",
                ["united states"] = "us", ["usa"] = "us", ["america"] = "us", ["сша"] = "us", ["америка"] = "us", ["ashburn"] = "us", ["dallas"] = "us", ["miami"] = "us", ["new york"] = "us", ["los angeles"] = "us", ["chicago"] = "us", ["seattle"] = "us",
                ["germany"] = "de", ["германия"] = "de", ["deutschland"] = "de", ["frankfurt"] = "de", ["франкфурт"] = "de", ["berlin"] = "de", ["берлин"] = "de", ["nuremberg"] = "de", ["falkenstein"] = "de", ["munich"] = "de",
                ["netherlands"] = "nl", ["нидерланды"] = "nl", ["голландия"] = "nl", ["holland"] = "nl", ["amsterdam"] = "nl", ["амстердам"] = "nl", ["meppel"] = "nl", ["rotterdam"] = "nl",
                ["finland"] = "fi", ["финляндия"] = "fi", ["helsinki"] = "fi", ["хельсинки"] = "fi",
                ["estonia"] = "ee", ["эстония"] = "ee", ["tallinn"] = "ee", ["таллин"] = "ee",
                ["poland"] = "pl", ["польша"] = "pl", ["warsaw"] = "pl", ["варшава"] = "pl",
                ["latvia"] = "lv", ["латвия"] = "lv", ["riga"] = "lv", ["рига"] = "lv",
                ["france"] = "fr", ["франция"] = "fr", ["paris"] = "fr", ["париж"] = "fr",
                ["united kingdom"] = "gb", ["britain"] = "gb", ["london"] = "gb", ["england"] = "gb", ["великобритания"] = "gb", ["англия"] = "gb", ["лондон"] = "gb",
                ["japan"] = "jp", ["япония"] = "jp", ["tokyo"] = "jp", ["токио"] = "jp", ["osaka"] = "jp",
                ["singapore"] = "sg", ["сингапур"] = "sg",
                ["hong kong"] = "hk", ["hongkong"] = "hk", ["гонконг"] = "hk",
                ["korea"] = "kr", ["seoul"] = "kr", ["корея"] = "kr", ["сеул"] = "kr",
                ["sweden"] = "se", ["швеция"] = "se", ["stockholm"] = "se", ["стокгольм"] = "se",
                ["switzerland"] = "ch", ["швейцария"] = "ch", ["zurich"] = "ch", ["цюрих"] = "ch",
                ["austria"] = "at", ["австрия"] = "at", ["vienna"] = "at", ["вена"] = "at",
                ["canada"] = "ca", ["канада"] = "ca", ["toronto"] = "ca", ["montreal"] = "ca",
                ["australia"] = "au", ["австралия"] = "au", ["sydney"] = "au", ["melbourne"] = "au",
                ["italy"] = "it", ["италия"] = "it", ["milan"] = "it", ["rome"] = "it",
                ["spain"] = "es", ["испания"] = "es", ["madrid"] = "es", ["barcelona"] = "es",
                ["india"] = "in", ["индия"] = "in", ["mumbai"] = "in",
                ["brazil"] = "br", ["бразилия"] = "br", ["sao paulo"] = "br",
                ["turkey"] = "tr", ["turkiye"] = "tr", ["türkiye"] = "tr", ["турция"] = "tr", ["istanbul"] = "tr", ["стамбул"] = "tr",
                ["ukraine"] = "ua", ["украина"] = "ua", ["kiev"] = "ua", ["kyiv"] = "ua", ["киев"] = "ua",
                ["kazakhstan"] = "kz", ["казахстан"] = "kz", ["almaty"] = "kz", ["алматы"] = "kz", ["astana"] = "kz", ["астана"] = "kz",
                ["armenia"] = "am", ["армения"] = "am", ["yerevan"] = "am", ["ереван"] = "am",
                ["georgia"] = "ge", ["грузия"] = "ge", ["tbilisi"] = "ge", ["тбилиси"] = "ge",
                ["uae"] = "ae", ["оаэ"] = "ae", ["dubai"] = "ae", ["дубай"] = "ae",
                ["belarus"] = "by", ["беларусь"] = "by", ["minsk"] = "by", ["минск"] = "by",
                ["romania"] = "ro", ["румыния"] = "ro", ["bucharest"] = "ro",
                ["bulgaria"] = "bg", ["болгария"] = "bg", ["sofia"] = "bg",
                ["portugal"] = "pt", ["португалия"] = "pt", ["lisbon"] = "pt",
                ["greece"] = "gr", ["греция"] = "gr", ["athens"] = "gr",
                ["hungary"] = "hu", ["венгрия"] = "hu", ["budapest"] = "hu",
                ["ireland"] = "ie", ["ирландия"] = "ie", ["dublin"] = "ie",
                ["czech"] = "cz", ["чехия"] = "cz", ["prague"] = "cz", ["прага"] = "cz",
                ["norway"] = "no", ["норвегия"] = "no", ["oslo"] = "no",
                ["denmark"] = "dk", ["дания"] = "dk", ["copenhagen"] = "dk",
                ["belgium"] = "be", ["бельгия"] = "be", ["brussels"] = "be",
                ["slovakia"] = "sk", ["словакия"] = "sk", ["slovenia"] = "si", ["словения"] = "si",
                ["croatia"] = "hr", ["хорватия"] = "hr", ["serbia"] = "rs", ["сербия"] = "rs", ["belgrade"] = "rs",
                ["lithuania"] = "lt", ["литва"] = "lt", ["vilnius"] = "lt", ["вильнюс"] = "lt",
                ["israel"] = "il", ["израиль"] = "il", ["tel aviv"] = "il",
                ["cyprus"] = "cy", ["кипр"] = "cy",
                ["moldova"] = "md", ["молдова"] = "md",
                ["iceland"] = "is", ["исландия"] = "is",
                ["luxembourg"] = "lu", ["люксембург"] = "lu",
                ["uzbekistan"] = "uz", ["узбекистан"] = "uz", ["tashkent"] = "uz",
                ["azerbaijan"] = "az", ["азербайджан"] = "az", ["baku"] = "az",
                ["vietnam"] = "vn", ["вьетнам"] = "vn",
                ["thailand"] = "th", ["таиланд"] = "th", ["bangkok"] = "th",
                ["taiwan"] = "tw", ["тайвань"] = "tw",
                ["indonesia"] = "id", ["индонезия"] = "id", ["jakarta"] = "id",
                ["malaysia"] = "my", ["малайзия"] = "my",
                ["mexico"] = "mx", ["мексика"] = "mx",
                ["argentina"] = "ar", ["аргентина"] = "ar",
                ["south africa"] = "za", ["юар"] = "za"
            };
            foreach (var kv in countryPatterns)
            {
                if (searchText.Contains(kv.Key))
                {
                    if (CountryNames.TryGetValue(kv.Value, out var res))
                        return (res.Flag, res.Name, kv.Value.ToUpperInvariant());
                }
            }

            // Explicit 2-letter uppercase country token in name (e.g. "NL #1", "[DE]", "FI-01", "UK")
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
            string hostLower = host.ToLowerInvariant();
            var hostParts = hostLower.Split('.');
            string tld = hostParts.Length > 0 ? hostParts[^1] : "";
            var tldCountries = new Dictionary<string, string>
            {
                ["ru"] = "ru", ["jp"] = "jp", ["de"] = "de", ["sg"] = "sg",
                ["hk"] = "hk", ["kr"] = "kr", ["nl"] = "nl", ["se"] = "se",
                ["ca"] = "ca", ["au"] = "au", ["fr"] = "fr", ["uk"] = "gb", ["gb"] = "gb",
                ["it"] = "it", ["es"] = "es", ["no"] = "no", ["fi"] = "fi", ["br"] = "br",
                ["in"] = "in", ["pl"] = "pl", ["cz"] = "cz", ["ch"] = "ch", ["at"] = "at",
                ["be"] = "be", ["dk"] = "dk", ["ee"] = "ee", ["lv"] = "lv", ["lt"] = "lt",
                ["ua"] = "ua", ["ro"] = "ro", ["bg"] = "bg", ["pt"] = "pt", ["gr"] = "gr",
                ["hu"] = "hu", ["ie"] = "ie", ["sk"] = "sk", ["si"] = "si", ["hr"] = "hr",
                ["rs"] = "rs", ["my"] = "my", ["id"] = "id", ["th"] = "th", ["tw"] = "tw",
                ["vn"] = "vn", ["ae"] = "ae", ["il"] = "il", ["za"] = "za", ["mx"] = "mx",
                ["ar"] = "ar", ["tr"] = "tr", ["kz"] = "kz", ["am"] = "am", ["ge"] = "ge",
                ["by"] = "by", ["md"] = "md", ["cy"] = "cy", ["is"] = "is", ["lu"] = "lu",
                ["uz"] = "uz", ["az"] = "az"
            };
            if (tldCountries.TryGetValue(tld, out string tldCode) && CountryNames.TryGetValue(tldCode, out var tldRes))
                return (tldRes.Flag, tldRes.Name, tldCode.ToUpperInvariant());

            return ("\U0001F310", "Unknown", "");
        }

        public static List<ServerInfo> DeduplicateNames(List<ServerInfo> servers)
        {
            var seen = new Dictionary<string, int>();
            foreach (var s in servers)
            {
                int count = seen.GetValueOrDefault(s.Name, 0) + 1;
                seen[s.Name] = count;
                if (count > 1)
                    s.Name = $"{s.Name} ({count})";
            }
            return servers;
        }
    }
}
