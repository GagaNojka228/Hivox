using System.Net;
using System.Text;
using System.Web;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.Models;

namespace Hivox.Mobile.Services;

public static class ConfigBuilder
{
    private static string SafeUnescape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Uri.UnescapeDataString(s); }
        catch
        {
            try { return HttpUtility.UrlDecode(s); }
            catch { return s; }
        }
    }

    private static string FixBase64(string s)
    {
        s = s.Trim().Replace('-', '+').Replace('_', '/');
        int padding = (4 - s.Length % 4) % 4;
        if (padding > 0) s += new string('=', padding);
        return s;
    }

    private static (string Host, int Port) ParseHostPort(string hp, int defaultPort = 443)
    {
        hp = hp.Trim().Split('/')[0];
        if (hp.StartsWith("["))
        {
            int idx = hp.IndexOf("]:");
            if (idx != -1 && int.TryParse(hp.Substring(idx + 2), out int p6))
                return (hp.Substring(1, idx - 1), p6);
            int endB = hp.IndexOf(']');
            if (endB > 1) return (hp.Substring(1, endB - 1), defaultPort);
        }
        if (hp.Contains(':'))
        {
            int lastColon = hp.LastIndexOf(':');
            string host = hp.Substring(0, lastColon);
            if (int.TryParse(hp.Substring(lastColon + 1), out int port))
                return (host, port);
            return (host, defaultPort);
        }
        return (hp, defaultPort);
    }

    private static Dictionary<string, string> ParseUriParams(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        foreach (string p in query.Split('&'))
        {
            if (string.IsNullOrEmpty(p)) continue;
            var parts = p.Split('=', 2);
            if (parts.Length == 2)
                result[parts[0]] = SafeUnescape(parts[1]);
            else if (parts.Length == 1)
                result[parts[0]] = "";
        }
        return result;
    }

    private static bool IsInsecureParam(Dictionary<string, string> p)
    {
        return p.GetValueOrDefault("allowInsecure", "") is "1" or "true" ||
               p.GetValueOrDefault("insecure", "") is "1" or "true" ||
               p.GetValueOrDefault("allow_insecure", "") is "1" or "true";
    }

    private static JObject CreateSockOpt(bool fragmentation = false)
    {
        var sockopt = new JObject
        {
            ["tcpFastOpen"] = true,
            ["tcpNoDelay"] = true,
            ["tcpKeepAliveInterval"] = Config.TcpKeepaliveInterval
        };
        if (fragmentation)
            sockopt["dialerProxy"] = "fragment";
        return sockopt;
    }

    private static bool IsValidIntOrRange(string? raw, int minAllowed)
    {
        string s = (raw ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+(-\d+)?$"))
            return false;
        var parts = s.Split('-', 2);
        if (!int.TryParse(parts[0], out int a) || a < minAllowed)
            return false;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out int b) || b < a))
            return false;
        return true;
    }

    private static JArray BuildNoisesArray()
    {
        string type = string.IsNullOrWhiteSpace(Config.NoiseType) ? "rand" : Config.NoiseType.Trim().ToLowerInvariant();
        string packet = string.IsNullOrWhiteSpace(Config.NoisePacket) ? "50-100" : Config.NoisePacket.Trim();
        string delay = string.IsNullOrWhiteSpace(Config.NoiseDelay) ? "10-15" : Config.NoiseDelay.Trim();

        if (type == "base64")
        {
            try { Convert.FromBase64String(packet); }
            catch { packet = Convert.ToBase64String(Encoding.UTF8.GetBytes(packet)); }
        }
        else if (type == "rand")
        {
            if (!IsValidIntOrRange(packet, 1))
                packet = "50-100";
        }

        if (!IsValidIntOrRange(delay, 0))
            delay = "10-15";

        return new JArray
        {
            new JObject
            {
                ["type"] = type,
                ["packet"] = packet,
                ["delay"] = delay
            }
        };
    }

    private static List<string> BuildDirectDnsDomains(ServerInfo server)
    {
        var directDnsDomains = Config.BypassRu ? Config.RuDomains.ToList() : new List<string>();
        if (!string.IsNullOrWhiteSpace(server.Host) && !IPAddress.TryParse(server.Host.Trim('[', ']'), out _))
            directDnsDomains.Add($"full:{server.Host.Trim()}");
        return directDnsDomains;
    }

    private static JArray BuildMobileInbounds(ConnectionMode connectionMode, bool httpEnabled, bool socksEnabled)
    {
        var inbounds = new JArray();
        string listenAddr = Config.AllowLan ? "0.0.0.0" : "127.0.0.1";
        bool hasAuth = Config.ProxyAuthEnabled &&
                       !string.IsNullOrWhiteSpace(Config.ProxyAuthUser) &&
                       !string.IsNullOrWhiteSpace(Config.ProxyAuthPassword);

        if (connectionMode == ConnectionMode.Tun)
        {
            inbounds.Add(new JObject
            {
                ["protocol"] = "tun",
                ["tag"] = "tun-in",
                ["settings"] = new JObject
                {
                    ["name"] = "tun0",
                    ["mtu"] = Config.TunMtu
                },
                ["sniffing"] = new JObject
                {
                    ["enabled"] = true,
                    ["destOverride"] = new JArray("http", "tls", "quic"),
                    ["routeOnly"] = Config.SniffingRouteOnly
                }
            });
        }

        // Always expose local SOCKS/HTTP inbounds (needed for Proxy mode AND internal healthcheck/IP detect in TUN mode)
        if (socksEnabled || connectionMode == ConnectionMode.Tun)
        {
            var socksSettings = new JObject
            {
                ["udp"] = !Config.BlockUdp,
                ["auth"] = hasAuth ? "password" : "noauth"
            };
            if (hasAuth)
            {
                socksSettings["accounts"] = new JArray
                {
                    new JObject { ["user"] = Config.ProxyAuthUser, ["pass"] = Config.ProxyAuthPassword }
                };
            }

            inbounds.Add(new JObject
            {
                ["protocol"] = "socks",
                ["listen"] = listenAddr,
                ["port"] = Config.SocksPort,
                ["tag"] = "socks-in",
                ["settings"] = socksSettings,
                ["sniffing"] = new JObject
                {
                    ["enabled"] = true,
                    ["destOverride"] = new JArray("http", "tls", "quic"),
                    ["routeOnly"] = Config.SniffingRouteOnly
                }
            });
        }
        bool exposeHttpToLan = httpEnabled || !socksEnabled;
        string httpListen = exposeHttpToLan ? listenAddr : "127.0.0.1";
        var httpIn = new JObject
        {
            ["protocol"] = "http",
            ["listen"] = httpListen,
            ["port"] = Config.ProxyPort,
            ["tag"] = "http-in",
            ["sniffing"] = new JObject
            {
                ["enabled"] = true,
                ["destOverride"] = new JArray("http", "tls", "quic"),
                ["routeOnly"] = Config.SniffingRouteOnly
            }
        };
        if (hasAuth && Config.HttpAuthOnHotspot && Config.AllowLan && exposeHttpToLan)
        {
            httpIn["settings"] = new JObject
            {
                ["accounts"] = new JArray
                {
                    new JObject { ["user"] = Config.ProxyAuthUser, ["pass"] = Config.ProxyAuthPassword }
                }
            };
        }
        inbounds.Add(httpIn);
        return inbounds;
    }

    private static JObject? BuildFromRawJson(
        string rawJson,
        ServerInfo server,
        bool gameMode,
        ConnectionMode connectionMode,
        string preferredIp,
        bool httpEnabled,
        bool socksEnabled)
    {
        try
        {
            var doc = JObject.Parse(rawJson);
            if (doc["outbounds"] is not JArray outbounds || !outbounds.OfType<JObject>().Any(o => o["protocol"] != null))
                return null;

            doc.Remove("remarks");
            doc.Remove("meta");
            doc.Remove("ps");
            doc.Remove("name");
            doc.Remove("id");

            doc["inbounds"] = BuildMobileInbounds(connectionMode, httpEnabled, socksEnabled);

            if (doc["log"] is not JObject logObj)
                doc["log"] = new JObject { ["loglevel"] = gameMode ? "error" : "warning" };
            else if (gameMode)
                logObj["loglevel"] = "error";

            string queryStrategy = preferredIp switch
            {
                "ipv6" => "UseIPv6",
                "ipv4" => "UseIPv4",
                _ => "UseIPv4"
            };
            var directDnsDomains = BuildDirectDnsDomains(server);

            if (connectionMode == ConnectionMode.Tun)
            {
                var dnsOutbound = outbounds.OfType<JObject>().FirstOrDefault(o =>
                    string.Equals(o["protocol"]?.ToString(), "dns", StringComparison.OrdinalIgnoreCase));
                string dnsOutTag;
                if (dnsOutbound != null)
                {
                    dnsOutTag = dnsOutbound["tag"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(dnsOutTag))
                    {
                        dnsOutTag = "dns-out";
                        dnsOutbound["tag"] = dnsOutTag;
                    }
                }
                else
                {
                    dnsOutTag = "dns-out";
                    outbounds.Add(new JObject { ["protocol"] = "dns", ["tag"] = dnsOutTag });
                }

                var directOutbound = outbounds.OfType<JObject>().FirstOrDefault(o =>
                    string.Equals(o["protocol"]?.ToString(), "freedom", StringComparison.OrdinalIgnoreCase));
                string directTag;
                if (directOutbound != null)
                {
                    directTag = directOutbound["tag"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(directTag))
                    {
                        directTag = "direct";
                        directOutbound["tag"] = directTag;
                    }
                }
                else
                {
                    directTag = "direct";
                    outbounds.Add(new JObject { ["protocol"] = "freedom", ["tag"] = directTag });
                }

                if (doc["routing"] is not JObject routingObj)
                {
                    routingObj = new JObject { ["domainStrategy"] = "IpIfNonMatch" };
                    doc["routing"] = routingObj;
                }
                if (routingObj["rules"] is not JArray rules)
                {
                    rules = new JArray();
                    routingObj["rules"] = rules;
                }

                rules.Insert(0, new JObject
                {
                    ["type"] = "field",
                    ["ip"] = new JArray("77.88.8.8"),
                    ["port"] = "53",
                    ["outboundTag"] = directTag
                });
                rules.Insert(0, new JObject
                {
                    ["type"] = "field",
                    ["inboundTag"] = new JArray("tun-in"),
                    ["port"] = "53",
                    ["outboundTag"] = dnsOutTag
                });

                int injectIdx = 2;
                if (Config.BlockUdp)
                {
                    var blockOutbound = outbounds.OfType<JObject>().FirstOrDefault(o =>
                        string.Equals(o["protocol"]?.ToString(), "blackhole", StringComparison.OrdinalIgnoreCase));
                    string blockTag = blockOutbound?["tag"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(blockTag))
                    {
                        blockTag = "block";
                        outbounds.Add(new JObject { ["protocol"] = "blackhole", ["tag"] = blockTag });
                    }
                    rules.Insert(Math.Min(injectIdx, rules.Count), new JObject
                    {
                        ["type"] = "field",
                        ["network"] = "udp",
                        ["outboundTag"] = blockTag
                    });
                    injectIdx++;
                }

                if (Config.BypassRu)
                {
                    rules.Insert(Math.Min(injectIdx, rules.Count), new JObject
                    {
                        ["type"] = "field",
                        ["domain"] = new JArray(Config.RuDomains),
                        ["outboundTag"] = directTag
                    });
                    injectIdx++;
                }

                if (Config.BypassLan)
                {
                    rules.Insert(Math.Min(injectIdx, rules.Count), new JObject
                    {
                        ["type"] = "field",
                        ["ip"] = new JArray("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128", "fc00::/7"),
                        ["outboundTag"] = directTag
                    });
                }

                if (doc["dns"] is not JObject dnsObj)
                {
                    string primaryDns = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns.Trim();
                    string primaryDnsAddr = Config.BlockUdp && !primaryDns.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) &&
                                            !primaryDns.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        ? $"tcp://{primaryDns}"
                        : primaryDns;
                    string directDnsAddr = Config.BlockUdp ? "tcp+local://77.88.8.8" : "77.88.8.8";
                    doc["dns"] = new JObject
                    {
                        ["servers"] = new JArray
                        {
                            new JObject { ["address"] = primaryDnsAddr },
                            new JObject
                            {
                                ["address"] = directDnsAddr,
                                ["domains"] = new JArray(directDnsDomains)
                            }
                        },
                        ["queryStrategy"] = queryStrategy
                    };
                }
                else
                {
                    static string EnforceTcpDns(string addr)
                    {
                        if (string.Equals(addr, "localhost", StringComparison.OrdinalIgnoreCase))
                            addr = "77.88.8.8";
                        if (!Config.BlockUdp || string.IsNullOrWhiteSpace(addr))
                            return addr;
                        if (addr.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                            return "tcp://" + addr[6..];
                        if (IPAddress.TryParse(addr, out _))
                            return $"tcp://{addr}";
                        return addr;
                    }

                    if (dnsObj["servers"] is JArray dnsServers)
                    {
                        for (int i = 0; i < dnsServers.Count; i++)
                        {
                            if (dnsServers[i] is JValue jv)
                            {
                                dnsServers[i] = EnforceTcpDns(jv.ToString());
                            }
                            else if (dnsServers[i] is JObject srvObj)
                            {
                                string addr = EnforceTcpDns(srvObj["address"]?.ToString() ?? "");
                                if (!string.IsNullOrWhiteSpace(addr))
                                    srvObj["address"] = addr;
                            }
                        }
                    }
                    if (preferredIp != "auto")
                        dnsObj["queryStrategy"] = queryStrategy;
                }
            }
            else if (preferredIp != "auto" && doc["dns"] is JObject proxyDnsObj)
            {
                proxyDnsObj["queryStrategy"] = queryStrategy;
            }

            return doc;
        }
        catch (Exception ex)
        {
            Logger.Log($"Raw JSON config fallback: {ex.Message}");
            return null;
        }
    }

    public static JObject? BuildConfig(
        ServerInfo server,
        bool gameMode = false,
        ConnectionMode connectionMode = ConnectionMode.Tun,
        bool muxEnabled = true,
        string preferredIp = "auto",
        bool fragmentation = false,
        bool httpEnabled = true,
        bool socksEnabled = true)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(server.RawJsonConfig))
            {
                var rawCfg = BuildFromRawJson(
                    server.RawJsonConfig, server, gameMode, connectionMode, preferredIp, httpEnabled, socksEnabled);
                if (rawCfg != null)
                    return rawCfg;
            }

            string uri = (server.Uri ?? "").Trim();
            string uriLower = uri.ToLowerInvariant();

            if (uriLower.StartsWith("tuic://"))
            {
                Logger.Error("TUIC protocol is not supported by Xray-core.");
                return null;
            }

            JObject? outbound = null;
            if (uriLower.StartsWith("vmess://"))
                outbound = BuildVmessOutbound(uri, fragmentation);
            else if (uriLower.StartsWith("vless://"))
                outbound = BuildVlessOutbound(uri, fragmentation);
            else if (uriLower.StartsWith("trojan://"))
                outbound = BuildTrojanOutbound(uri, fragmentation);
            else if (uriLower.StartsWith("ss://"))
                outbound = BuildShadowsocksOutbound(uri, fragmentation);
            else if (uriLower.StartsWith("hysteria2://") || uriLower.StartsWith("hy2://"))
                outbound = BuildHysteria2Outbound(uri);
            else if (uriLower.StartsWith("socks://") || uriLower.StartsWith("socks5://"))
                outbound = BuildSocksOutbound(uri);

            if (outbound == null)
            {
                Logger.Error($"Unsupported protocol: {uri[..Math.Min(30, uri.Length)]}");
                return null;
            }

            outbound["tag"] = "proxy";

            // Enable Xray Mux only on TCP-based protocols without XTLS flow
            bool isHysteria = outbound["protocol"]?.ToString() == "hysteria";
            string vlessFlow = outbound["settings"]?["vnext"]?[0]?["users"]?[0]?["flow"]?.ToString() ?? "";
            if (muxEnabled && !isHysteria && string.IsNullOrEmpty(vlessFlow))
            {
                outbound["mux"] = new JObject
                {
                    ["enabled"] = true,
                    ["concurrency"] = Math.Clamp(Config.MuxConcurrency, 1, 64),
                    ["xudpConcurrency"] = Math.Clamp(Config.MuxXudpConcurrency, 1, 128),
                    ["xudpProxyUDP443"] = string.IsNullOrWhiteSpace(Config.MuxXudpQuic) ? "reject" : Config.MuxXudpQuic
                };
            }

            string queryStrategy = preferredIp switch
            {
                "ipv6" => "UseIPv6",
                "ipv4" => "UseIPv4",
                _ => "UseIPv4"
            };

            string freedomStrategy = preferredIp switch
            {
                "ipv6" => "UseIPv6",
                "ipv4" => "UseIPv4",
                _ => "AsIs"
            };

            // Collect server domain for direct DNS bootstrap (prevents DNS loop for proxy server hostname)
            var directDnsDomains = BuildDirectDnsDomains(server);

            var inbounds = BuildMobileInbounds(connectionMode, httpEnabled, socksEnabled);

            var directSettings = new JObject { ["domainStrategy"] = freedomStrategy };
            if (Config.NoisesEnabled)
                directSettings["noises"] = BuildNoisesArray();

            var outbounds = new JArray
            {
                outbound,
                new JObject
                {
                    ["protocol"] = "freedom",
                    ["tag"] = "direct",
                    ["settings"] = directSettings
                },
                new JObject { ["protocol"] = "blackhole", ["tag"] = "block" },
                new JObject { ["protocol"] = "dns", ["tag"] = "dns-out" }
            };

            if (fragmentation && !isHysteria)
            {
                string fragLen = (Config.FragmentLength ?? "").Trim();
                if (!IsValidIntOrRange(fragLen, 1))
                    fragLen = "100-200";
                string fragInt = (Config.FragmentInterval ?? "").Trim();
                if (!IsValidIntOrRange(fragInt, 0))
                    fragInt = "10-20";

                var fragSettings = new JObject
                {
                    ["domainStrategy"] = freedomStrategy,
                    ["fragment"] = new JObject
                    {
                        ["packets"] = string.IsNullOrWhiteSpace(Config.FragmentPackets) ? "tlshello" : Config.FragmentPackets,
                        ["length"] = fragLen,
                        ["interval"] = fragInt
                    }
                };
                if (Config.NoisesEnabled)
                    fragSettings["noises"] = BuildNoisesArray();

                outbounds.Add(new JObject
                {
                    ["protocol"] = "freedom",
                    ["tag"] = "fragment",
                    ["settings"] = fragSettings,
                    ["streamSettings"] = new JObject
                    {
                        ["sockopt"] = new JObject
                        {
                            ["tcpNoDelay"] = true
                        }
                    }
                });
            }

            var rules = new JArray();

            // In TUN mode, hijack DNS UDP/53 from tun-in into Xray's DNS module
            if (connectionMode == ConnectionMode.Tun)
            {
                rules.Add(new JObject
                {
                    ["type"] = "field",
                    ["inboundTag"] = new JArray("tun-in"),
                    ["port"] = "53",
                    ["outboundTag"] = "dns-out"
                });
            }

            // Direct DNS server IP so Xray's own direct DNS lookup doesn't loop
            rules.Add(new JObject
            {
                ["type"] = "field",
                ["ip"] = new JArray("77.88.8.8"),
                ["port"] = "53",
                ["outboundTag"] = "direct"
            });

            // Russian domains bypass (must keep "domain:" prefix for Xray!)
            if (Config.BypassRu)
            {
                rules.Add(new JObject
                {
                    ["type"] = "field",
                    ["domain"] = new JArray(Config.RuDomains),
                    ["outboundTag"] = "direct"
                });
            }

            // Private / LAN CIDRs direct
            if (Config.BypassLan)
            {
                rules.Add(new JObject
                {
                    ["type"] = "field",
                    ["ip"] = new JArray("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128", "fc00::/7"),
                    ["outboundTag"] = "direct"
                });
            }

            // Block UDP / QUIC / Voice UDP if explicitly enabled (DNS uses TCP when BlockUdp is active)
            if (Config.BlockUdp)
            {
                rules.Add(new JObject
                {
                    ["type"] = "field",
                    ["network"] = "udp",
                    ["outboundTag"] = "block"
                });
            }

            // Catch-all through proxy
            rules.Add(new JObject
            {
                ["type"] = "field",
                ["network"] = Config.BlockUdp ? "tcp" : "tcp,udp",
                ["outboundTag"] = "proxy"
            });

            string primaryDns = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns.Trim();
            string primaryDnsAddr = Config.BlockUdp && !primaryDns.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) &&
                                    !primaryDns.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? $"tcp://{primaryDns}"
                : primaryDns;
            string directDnsAddr = Config.BlockUdp ? "tcp+local://77.88.8.8" : "77.88.8.8";
            var dnsServers = new JArray
            {
                new JObject { ["address"] = primaryDnsAddr }
            };
            if (directDnsDomains.Count > 0)
            {
                dnsServers.Add(new JObject
                {
                    ["address"] = directDnsAddr,
                    ["domains"] = new JArray(directDnsDomains)
                });
            }

            var config = new JObject
            {
                ["log"] = new JObject
                {
                    ["loglevel"] = gameMode ? "error" : "warning"
                },
                ["dns"] = new JObject
                {
                    ["servers"] = dnsServers,
                    ["queryStrategy"] = queryStrategy
                },
                ["inbounds"] = inbounds,
                ["outbounds"] = outbounds,
                ["routing"] = new JObject
                {
                    ["domainStrategy"] = "IpIfNonMatch",
                    ["rules"] = rules
                }
            };

            return config;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to build config: {ex.Message}");
            return null;
        }
    }

    private static JObject BuildVmessOutbound(string uri, bool fragmentation)
    {
        int hashIdx = uri.IndexOf('#');
        string raw = hashIdx >= 0 ? uri[8..hashIdx] : uri[8..];
        var data = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(FixBase64(raw))));
        string net = (data["net"]?.ToString() ?? "tcp").Trim().ToLowerInvariant();
        string tlsVal = (data["tls"]?.ToString() ?? "").Trim().ToLowerInvariant();
        string security = tlsVal is "tls" or "true" or "1" ? "tls" : "none";

        var streamSettings = new JObject { ["network"] = net, ["security"] = security };
        ApplyTransportSettings(streamSettings, net, data["path"]?.ToString() ?? "/",
            data["host"]?.ToString() ?? "", data["path"]?.ToString() ?? "", data["mode"]?.ToString() ?? "auto");

        if (security == "tls")
        {
            string sni = data["sni"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(sni)) sni = data["host"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(sni)) sni = data["add"]?.ToString() ?? "";
            streamSettings["tlsSettings"] = new JObject
            {
                ["serverName"] = sni,
                ["alpn"] = new JArray("h2", "http/1.1")
            };
        }

        streamSettings["sockopt"] = CreateSockOpt(fragmentation);

        return new JObject
        {
            ["protocol"] = "vmess",
            ["settings"] = new JObject
            {
                ["vnext"] = new JArray
                {
                    new JObject
                    {
                        ["address"] = data["add"]?.ToString() ?? "",
                        ["port"] = int.TryParse(data["port"]?.ToString(), out int p) ? p : 443,
                        ["users"] = new JArray
                        {
                            new JObject
                            {
                                ["id"] = data["id"]?.ToString() ?? "",
                                ["alterId"] = int.TryParse(data["aid"]?.ToString(), out int aid) ? aid : 0,
                                ["security"] = string.IsNullOrEmpty(data["scy"]?.ToString()) ? "auto" : data["scy"]!.ToString()
                            }
                        }
                    }
                }
            },
            ["streamSettings"] = streamSettings
        };
    }

    private static JObject? BuildVlessOutbound(string uri, bool fragmentation)
    {
        string uriClean = uri[8..].Split('#')[0];
        int atIdx = uriClean.IndexOf('@');
        if (atIdx <= 0) return null;
        string uuid = SafeUnescape(uriClean[..atIdx]);
        string rest = uriClean[(atIdx + 1)..];
        string hp = rest.Split('?')[0];
        var (host, port) = ParseHostPort(hp);
        var p = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");

        string transportType = p.GetValueOrDefault("type", "tcp").ToLowerInvariant();
        if (transportType == "raw") transportType = "tcp";
        string security = p.GetValueOrDefault("security", "none").ToLowerInvariant();

        var streamSettings = new JObject
        {
            ["network"] = transportType,
            ["security"] = security
        };

        ApplyTransportSettings(streamSettings, transportType,
            p.GetValueOrDefault("path", "/"),
            p.GetValueOrDefault("host", ""),
            p.GetValueOrDefault("serviceName", p.GetValueOrDefault("path", "")),
            p.GetValueOrDefault("mode", "auto"),
            p.GetValueOrDefault("extra", ""));

        if (security == "tls")
        {
            string sni = p.GetValueOrDefault("sni", "");
            if (string.IsNullOrEmpty(sni)) sni = host;
            var alpnRaw = p.GetValueOrDefault("alpn", "");
            var alpnItems = string.IsNullOrWhiteSpace(alpnRaw)
                ? new[] { "h2", "http/1.1" }
                : alpnRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (alpnItems.Length == 0) alpnItems = new[] { "h2", "http/1.1" };
            var tlsObj = new JObject
            {
                ["serverName"] = sni,
                ["alpn"] = new JArray(alpnItems)
            };
            if (IsInsecureParam(p)) tlsObj["allowInsecure"] = true;
            if (p.TryGetValue("fp", out var fp) && !string.IsNullOrEmpty(fp)) tlsObj["fingerprint"] = fp;
            streamSettings["tlsSettings"] = tlsObj;
        }
        else if (security == "reality")
        {
            string realityFp = p.GetValueOrDefault("fp", "");
            if (string.IsNullOrWhiteSpace(realityFp)) realityFp = "chrome";
            string spiderX = p.GetValueOrDefault("spiderX", p.GetValueOrDefault("spx", ""));
            if (string.IsNullOrWhiteSpace(spiderX)) spiderX = "/";
            streamSettings["realitySettings"] = new JObject
            {
                ["serverName"] = p.GetValueOrDefault("sni", ""),
                ["fingerprint"] = realityFp,
                ["publicKey"] = p.GetValueOrDefault("pbk", ""),
                ["shortId"] = p.GetValueOrDefault("sid", ""),
                ["spiderX"] = spiderX
            };
        }

        streamSettings["sockopt"] = CreateSockOpt(fragmentation);

        return new JObject
        {
            ["protocol"] = "vless",
            ["settings"] = new JObject
            {
                ["vnext"] = new JArray
                {
                    new JObject
                    {
                        ["address"] = host,
                        ["port"] = port,
                        ["users"] = new JArray
                        {
                            new JObject
                            {
                                ["id"] = uuid,
                                ["encryption"] = p.GetValueOrDefault("encryption", "none"),
                                ["flow"] = p.GetValueOrDefault("flow", "")
                            }
                        }
                    }
                }
            },
            ["streamSettings"] = streamSettings
        };
    }

    private static JObject? BuildTrojanOutbound(string uri, bool fragmentation)
    {
        string uriClean = uri[9..].Split('#')[0];
        int atIdx = uriClean.LastIndexOf('@');
        if (atIdx <= 0) return null;
        string password = SafeUnescape(uriClean[..atIdx]);
        string rest = uriClean[(atIdx + 1)..];
        string hp = rest.Split('?')[0];
        var (host, port) = ParseHostPort(hp);
        var p = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");

        string net = p.GetValueOrDefault("type", "tcp").ToLowerInvariant();
        if (net == "raw") net = "tcp";
        string security = p.GetValueOrDefault("security", "tls").ToLowerInvariant();

        var streamSettings = new JObject
        {
            ["network"] = net,
            ["security"] = security
        };

        ApplyTransportSettings(streamSettings, net,
            p.GetValueOrDefault("path", "/"),
            p.GetValueOrDefault("host", ""),
            p.GetValueOrDefault("serviceName", p.GetValueOrDefault("path", "")),
            p.GetValueOrDefault("mode", "auto"));

        if (security == "tls")
        {
            string sni = p.GetValueOrDefault("sni", "");
            if (string.IsNullOrEmpty(sni)) sni = host;
            var alpnRaw = p.GetValueOrDefault("alpn", "");
            var alpnItems = string.IsNullOrWhiteSpace(alpnRaw)
                ? new[] { "h2", "http/1.1" }
                : alpnRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (alpnItems.Length == 0) alpnItems = new[] { "h2", "http/1.1" };
            var tlsObj = new JObject
            {
                ["serverName"] = sni,
                ["alpn"] = new JArray(alpnItems)
            };
            if (IsInsecureParam(p)) tlsObj["allowInsecure"] = true;
            if (p.TryGetValue("fp", out var fp) && !string.IsNullOrEmpty(fp)) tlsObj["fingerprint"] = fp;
            streamSettings["tlsSettings"] = tlsObj;
        }
        else if (security == "reality")
        {
            string realityFp = p.GetValueOrDefault("fp", "");
            if (string.IsNullOrWhiteSpace(realityFp)) realityFp = "chrome";
            string spiderX = p.GetValueOrDefault("spiderX", p.GetValueOrDefault("spx", ""));
            if (string.IsNullOrWhiteSpace(spiderX)) spiderX = "/";
            streamSettings["realitySettings"] = new JObject
            {
                ["serverName"] = p.GetValueOrDefault("sni", ""),
                ["fingerprint"] = realityFp,
                ["publicKey"] = p.GetValueOrDefault("pbk", ""),
                ["shortId"] = p.GetValueOrDefault("sid", ""),
                ["spiderX"] = spiderX
            };
        }

        streamSettings["sockopt"] = CreateSockOpt(fragmentation);

        return new JObject
        {
            ["protocol"] = "trojan",
            ["settings"] = new JObject
            {
                ["servers"] = new JArray
                {
                    new JObject { ["address"] = host, ["port"] = port, ["password"] = password }
                }
            },
            ["streamSettings"] = streamSettings
        };
    }

    private static JObject? BuildShadowsocksOutbound(string uri, bool fragmentation)
    {
        string ssBody = uri[5..].Split('#')[0].Split('?')[0];
        string method, password, host;
        int port;

        if (ssBody.Contains('@'))
        {
            int atIdx = ssBody.LastIndexOf('@');
            string userInfo = ssBody[..atIdx];
            (host, port) = ParseHostPort(ssBody[(atIdx + 1)..]);
            if (userInfo.Contains(':'))
            {
                var mp = userInfo.Split(':', 2);
                method = SafeUnescape(mp[0]);
                password = SafeUnescape(mp[1]);
            }
            else
            {
                string unescapedUserInfo = SafeUnescape(userInfo);
                if (unescapedUserInfo.Contains(':'))
                {
                    var mp = unescapedUserInfo.Split(':', 2);
                    method = mp[0];
                    password = mp[1];
                }
                else
                {
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(FixBase64(userInfo)));
                    var mp = decoded.Split(':', 2);
                    if (mp.Length < 2) return null;
                    method = mp[0];
                    password = mp[1];
                }
            }
        }
        else
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(FixBase64(ssBody)));
            int atIdx = decoded.LastIndexOf('@');
            if (atIdx <= 0) return null;
            var mp = decoded[..atIdx].Split(':', 2);
            if (mp.Length < 2) return null;
            method = mp[0];
            password = mp[1];
            (host, port) = ParseHostPort(decoded[(atIdx + 1)..]);
        }

        return new JObject
        {
            ["protocol"] = "shadowsocks",
            ["settings"] = new JObject
            {
                ["servers"] = new JArray
                {
                    new JObject { ["address"] = host, ["port"] = port, ["method"] = method, ["password"] = password }
                }
            },
            ["streamSettings"] = new JObject
            {
                ["sockopt"] = CreateSockOpt(fragmentation)
            }
        };
    }

    private static JObject BuildHysteria2Outbound(string uri)
    {
        string rest = uri.Split(new[] { "://" }, 2, StringSplitOptions.None)[1].Split('#')[0];
        string auth = "";
        if (rest.Contains('@'))
        {
            int atIdx = rest.LastIndexOf('@');
            auth = SafeUnescape(rest[..atIdx]);
            rest = rest[(atIdx + 1)..];
        }
        string hp = rest.Split('?')[0];
        var (host, port) = ParseHostPort(hp);
        var p = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");

        string sni = p.GetValueOrDefault("sni", "");
        if (string.IsNullOrEmpty(sni)) sni = host;

        var tlsSettings = new JObject { ["serverName"] = sni };
        if (IsInsecureParam(p)) tlsSettings["allowInsecure"] = true;
        if (p.TryGetValue("alpn", out var alpn) && !string.IsNullOrWhiteSpace(alpn))
        {
            var alpnItems = alpn.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();
            if (alpnItems.Length > 0)
                tlsSettings["alpn"] = new JArray(alpnItems);
        }

        var hySettings = new JObject
        {
            ["version"] = 2,
            ["auth"] = auth
        };
        if (p.TryGetValue("upmbps", out var up) || p.TryGetValue("up_mbps", out up))
            hySettings["up"] = up;
        if (p.TryGetValue("downmbps", out var down) || p.TryGetValue("down_mbps", out down))
            hySettings["down"] = down;

        // Xray-core 26.x native Hysteria2 outbound schema
        return new JObject
        {
            ["protocol"] = "hysteria",
            ["settings"] = new JObject
            {
                ["version"] = 2,
                ["address"] = host,
                ["port"] = port
            },
            ["streamSettings"] = new JObject
            {
                ["network"] = "hysteria",
                ["security"] = "tls",
                ["tlsSettings"] = tlsSettings,
                ["hysteriaSettings"] = hySettings
            }
        };
    }

    private static JObject BuildSocksOutbound(string uri)
    {
        int sepIdx = uri.IndexOf("://", StringComparison.Ordinal);
        string rest = uri[(sepIdx + 3)..].Split('#')[0];
        string user = "", pass = "";
        if (rest.Contains('@'))
        {
            int atIdx = rest.LastIndexOf('@');
            string auth = rest[..atIdx];
            rest = rest[(atIdx + 1)..];
            if (auth.Contains(':'))
            {
                var ap = auth.Split(':', 2);
                user = SafeUnescape(ap[0]);
                pass = SafeUnescape(ap[1]);
            }
            else
            {
                try
                {
                    string dec = Encoding.UTF8.GetString(Convert.FromBase64String(FixBase64(auth)));
                    var ap = dec.Split(':', 2);
                    user = ap[0];
                    pass = ap.Length > 1 ? ap[1] : "";
                }
                catch { }
            }
        }
        var (host, port) = ParseHostPort(rest.Split('?')[0], 1080);
        var serverObj = new JObject { ["address"] = host, ["port"] = port };
        if (!string.IsNullOrEmpty(user))
            serverObj["users"] = new JArray { new JObject { ["user"] = user, ["pass"] = pass } };

        return new JObject
        {
            ["protocol"] = "socks",
            ["settings"] = new JObject { ["servers"] = new JArray { serverObj } }
        };
    }

    private static void ApplyTransportSettings(
        JObject streamSettings,
        string net,
        string path,
        string host,
        string serviceName,
        string mode,
        string extra = "")
    {
        if (net == "ws")
        {
            var ws = new JObject { ["path"] = string.IsNullOrEmpty(path) ? "/" : path };
            if (!string.IsNullOrEmpty(host))
                ws["headers"] = new JObject { ["Host"] = host };
            streamSettings["wsSettings"] = ws;
        }
        else if (net == "grpc")
        {
            streamSettings["grpcSettings"] = new JObject { ["serviceName"] = serviceName };
        }
        else if (net is "h2" or "http")
        {
            var http = new JObject { ["path"] = string.IsNullOrEmpty(path) ? "/" : path };
            if (!string.IsNullOrEmpty(host))
                http["host"] = new JArray(host.Split(',').Select(h => h.Trim()));
            streamSettings["httpSettings"] = http;
        }
        else if (net is "xhttp" or "splithttp")
        {
            streamSettings["network"] = "xhttp";
            var xh = new JObject
            {
                ["mode"] = string.IsNullOrEmpty(mode) ? "auto" : mode,
                ["path"] = string.IsNullOrEmpty(path) ? "/" : path
            };
            if (!string.IsNullOrEmpty(host)) xh["host"] = host;
            if (!string.IsNullOrEmpty(extra))
            {
                try { xh["extra"] = JObject.Parse(extra); } catch { }
            }
            streamSettings["xhttpSettings"] = xh;
        }
    }
}
