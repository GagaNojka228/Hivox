using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using Newtonsoft.Json.Linq;
using Hivox.Models;

namespace Hivox.Services
{
    public static class ConfigBuilder
    {
        private static string SafeUnescape(string s)
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

        private static (string Method, string Password, string Host, int Port) ParseShadowsocksUri(string uriClean)
        {
            string ssBody = uriClean.Substring(5).Split('?')[0];
            if (ssBody.Contains('@'))
            {
                int atIdx = ssBody.LastIndexOf('@');
                string userInfo = ssBody[..atIdx];
                string hp = ssBody[(atIdx + 1)..];
                var (host, port) = ParseHostPort(hp);
                if (userInfo.Contains(':'))
                {
                    var mp = userInfo.Split(':', 2);
                    return (SafeUnescape(mp[0]), SafeUnescape(mp[1]), host, port);
                }
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(FixBase64(userInfo)));
                var parts = decoded.Split(':', 2);
                return (parts[0], parts[1], host, port);
            }
            else
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(FixBase64(ssBody)));
                int atIdx = decoded.LastIndexOf('@');
                if (atIdx > 0)
                {
                    string mpPart = decoded.Substring(0, atIdx);
                    string hpPart = decoded.Substring(atIdx + 1);
                    var (host, port) = ParseHostPort(hpPart);
                    var parts = mpPart.Split(':', 2);
                    return (parts[0], parts[1], host, port);
                }
            }
            throw new Exception("Invalid Shadowsocks URI");
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

        private static JObject BuildTrojanStreamSettings(string host, Dictionary<string, string> paramsDict)
        {
            string security = paramsDict.GetValueOrDefault("security", "tls");
            string net = paramsDict.GetValueOrDefault("type", "tcp").ToLowerInvariant();
            var stream = new JObject
            {
                ["network"] = net,
                ["security"] = security
            };
            if (net == "ws")
            {
                var ws = new JObject { ["path"] = paramsDict.GetValueOrDefault("path", "/") };
                if (paramsDict.ContainsKey("host"))
                    ws["headers"] = new JObject { ["Host"] = paramsDict["host"] };
                stream["wsSettings"] = ws;
            }
            else if (net == "grpc")
            {
                stream["grpcSettings"] = new JObject { ["serviceName"] = paramsDict.GetValueOrDefault("serviceName", paramsDict.GetValueOrDefault("path", "")) };
            }
            if (security == "tls")
            {
                var tls = new JObject
                {
                    ["serverName"] = string.IsNullOrEmpty(paramsDict.GetValueOrDefault("sni", "")) ? host : paramsDict["sni"],
                    ["alpn"] = new JArray(paramsDict.GetValueOrDefault("alpn", "h2,http/1.1").Split(',').Select(x => x.Trim()))
                };
                if (IsInsecureParam(paramsDict)) tls["allowInsecure"] = true;
                if (paramsDict.ContainsKey("fp")) tls["fingerprint"] = paramsDict["fp"];
                stream["tlsSettings"] = tls;
            }
            if (security == "reality")
            {
                var rs = new JObject
                {
                    ["serverName"] = paramsDict.GetValueOrDefault("sni", ""),
                    ["fingerprint"] = paramsDict.GetValueOrDefault("fp", "chrome"),
                    ["publicKey"] = paramsDict.GetValueOrDefault("pbk", ""),
                    ["shortId"] = paramsDict.GetValueOrDefault("sid", "")
                };
                string spx = paramsDict.GetValueOrDefault("spiderX", paramsDict.GetValueOrDefault("spx", ""));
                if (!string.IsNullOrEmpty(spx)) rs["spiderX"] = spx;
                stream["realitySettings"] = rs;
            }
            return stream;
        }

        public static JObject BuildConfig(ServerInfo server, bool gameMode = false,
            ConnectionMode connectionMode = ConnectionMode.Proxy,
            CoreType coreType = CoreType.Xray, bool muxEnabled = true,
            string preferredIp = "auto", bool allowLan = false,
            bool fragmentation = false, bool httpEnabled = true, bool socksEnabled = true)
        {
            string domainStrategy = preferredIp switch
            {
                "ipv4" => "prefer_ipv4",
                "ipv6" => "prefer_ipv6",
                _ => "prefer_ipv4"
            };
            string listenAddr = allowLan ? "0.0.0.0" : "127.0.0.1";

            try
            {
                string uri = (server.Uri ?? "").Trim();
                string uriLower = uri.ToLowerInvariant();
                if (coreType == CoreType.Xray && connectionMode == ConnectionMode.Tun)
                    socksEnabled = true;
                if (connectionMode == ConnectionMode.Proxy)
                    httpEnabled = true;

                if (coreType == CoreType.Xray && !string.IsNullOrEmpty(server.RawJsonConfig))
                {
                    var rawCfg = BuildXrayFromRawJson(server.RawJsonConfig, gameMode, preferredIp, listenAddr, httpEnabled, socksEnabled);
                    if (rawCfg != null)
                        return rawCfg;
                }

                if (coreType == CoreType.Xray)
                {
                    if (uriLower.StartsWith("hysteria://") || uriLower.StartsWith("hysteria2://") || uriLower.StartsWith("hy2://") ||
                        uriLower.StartsWith("tuic://") || uriLower.StartsWith("wireguard://") || uriLower.StartsWith("wg://") ||
                        uriLower.StartsWith("anytls://"))
                    {
                        Logger.Log($"Protocol {server.Protocol} is not supported by Xray. Use Sing-box.");
                        return null;
                    }
                }

                if (coreType == CoreType.SingBox)
                    return BuildSingboxConfig(uri, server, connectionMode, domainStrategy, listenAddr, httpEnabled, socksEnabled, muxEnabled, gameMode);

                JObject config = null;
                if (uriLower.StartsWith("vmess://"))
                    config = BuildVmessConfig(uri);
                else if (uriLower.StartsWith("vless://"))
                    config = BuildVlessConfig(uri);
                else if (uriLower.StartsWith("trojan://"))
                    config = BuildTrojanConfig(uri);
                else if (uriLower.StartsWith("ss://"))
                    config = BuildShadowsocksConfig(uri);
                else if (uriLower.StartsWith("socks://") || uriLower.StartsWith("socks5://"))
                    config = BuildSocksConfig(uri);
                else if (uriLower.StartsWith("hysteria2://") || uriLower.StartsWith("hy2://"))
                    config = BuildHysteria2Config(uri);
                else if (uriLower.StartsWith("tuic://"))
                    config = BuildTuicConfig(uri);

                if (config != null)
                {
                    ApplyXrayInbounds(config, listenAddr, httpEnabled, socksEnabled);
                    config = ApplyOptimizations(config, gameMode, muxEnabled, fragmentation, preferredIp);
                    config = AddRussianBypass(config);
                }
                return config;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to build config: {ex.Message}");
                return null;
            }
        }

        private static JObject BuildXrayFromRawJson(string rawJson, bool gameMode, string preferredIp,
            string listenAddr, bool httpEnabled, bool socksEnabled)
        {
            try
            {
                var doc = JObject.Parse(rawJson);
                if (doc["outbounds"]?[0]?["protocol"] == null)
                    return null;

                doc.Remove("remarks");
                doc.Remove("meta");
                doc.Remove("ps");

                ApplyXrayInbounds(doc, listenAddr, httpEnabled, socksEnabled);

                if (gameMode)
                {
                    var log = doc["log"] as JObject ?? new JObject();
                    log["loglevel"] = "none";
                    doc["log"] = log;
                }

                if (preferredIp != "auto" && doc["dns"] is JObject dnsObj)
                {
                    dnsObj["queryStrategy"] = preferredIp == "ipv6" ? "UseIPv6" : "UseIPv4";
                }

                return doc;
            }
            catch (Exception ex)
            {
                Logger.Log($"Raw Xray JSON fallback: {ex.Message}");
                return null;
            }
        }

        private static void ApplyXrayInbounds(JObject config, string listenAddr, bool httpEnabled, bool socksEnabled)
        {
            var inbounds = new JArray();
            bool hasAuth = Config.ProxyAuthEnabled &&
                           !string.IsNullOrWhiteSpace(Config.ProxyAuthUser) &&
                           !string.IsNullOrWhiteSpace(Config.ProxyAuthPassword);

            if (socksEnabled)
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
                    ["tag"] = "socks",
                    ["listen"] = listenAddr,
                    ["port"] = Config.SocksPort,
                    ["protocol"] = "socks",
                    ["settings"] = socksSettings,
                    ["sniffing"] = new JObject
                    {
                        ["enabled"] = true,
                        ["destOverride"] = new JArray("http", "tls", "quic"),
                        ["routeOnly"] = Config.SniffingRouteOnly
                    }
                });
            }
            if (httpEnabled || inbounds.Count == 0)
            {
                var httpIn = new JObject
                {
                    ["tag"] = "http",
                    ["listen"] = listenAddr,
                    ["port"] = Config.ProxyPort,
                    ["protocol"] = "http",
                    ["sniffing"] = new JObject
                    {
                        ["enabled"] = true,
                        ["destOverride"] = new JArray("http", "tls", "quic"),
                        ["routeOnly"] = Config.SniffingRouteOnly
                    }
                };
                if (hasAuth && Config.HttpAuthOnHotspot && Config.AllowLan)
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
            }
            config["inbounds"] = inbounds;
        }

        private static JObject BuildHysteria2Config(string uri)
        {
            try
            {
                string uriPart = uri.Split(new[] { "://" }, StringSplitOptions.None)[1].Split('#')[0];
                string auth = "", host = "", portStr = "443";
                if (uriPart.Contains('@'))
                {
                    var parts = uriPart.Split('@', 2);
                    auth = parts[0];
                    string rest = parts[1];
                    host = rest.Split('?')[0];
                    if (host.Contains(':'))
                    {
                        var hp = host.Split(':');
                        host = hp[0];
                        portStr = hp[1];
                    }
                }
                var paramsDict = new Dictionary<string, string>();
                if (uriPart.Contains('?'))
                {
                    string paramStr = uriPart.Split('?')[1].Split('#')[0];
                    foreach (var p in paramStr.Split('&'))
                    {
                        var kv = p.Split('=');
                        if (kv.Length == 2) paramsDict[kv[0]] = kv[1];
                    }
                }
                return new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "warning" },
                    ["inbounds"] = new JArray
                    {
                        new JObject { ["port"] = Config.SocksPort, ["protocol"] = "socks", ["settings"] = new JObject { ["udp"] = true } },
                        new JObject { ["port"] = Config.ProxyPort, ["protocol"] = "http" }
                    },
                    ["outbounds"] = new JArray
                    {
                        new JObject
                        {
                            ["protocol"] = "hysteria2",
                            ["settings"] = new JObject
                            {
                                ["server"] = $"{host}:{portStr}",
                                ["password"] = auth
                            },
                            ["streamSettings"] = new JObject
                            {
                                ["network"] = "h2",
                                ["security"] = "tls",
                                ["tlsSettings"] = new JObject
                                {
                                    ["serverName"] = paramsDict.GetValueOrDefault("sni", host)
                                }
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"Hysteria2 config error: {ex.Message}");
                return null;
            }
        }

        private static JObject BuildTuicConfig(string uri)
        {
            try
            {
                string uriPart = uri.Split(new[] { "://" }, StringSplitOptions.None)[1].Split('#')[0];
                string uuid = "", host = "", portStr = "443";
                if (uriPart.Contains('@'))
                {
                    var parts = uriPart.Split('@', 2);
                    uuid = parts[0];
                    string rest = parts[1];
                    host = rest.Split('?')[0];
                    if (host.Contains(':'))
                    {
                        var hp = host.Split(':');
                        host = hp[0];
                        portStr = hp[1];
                    }
                }
                var paramsDict = new Dictionary<string, string>();
                if (uriPart.Contains('?'))
                {
                    string paramStr = uriPart.Split('?')[1].Split('#')[0];
                    foreach (var p in paramStr.Split('&'))
                    {
                        var kv = p.Split('=');
                        if (kv.Length == 2) paramsDict[kv[0]] = kv[1];
                    }
                }
                return new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "warning" },
                    ["inbounds"] = new JArray
                    {
                        new JObject { ["port"] = Config.SocksPort, ["protocol"] = "socks", ["settings"] = new JObject { ["udp"] = true } },
                        new JObject { ["port"] = Config.ProxyPort, ["protocol"] = "http" }
                    },
                    ["outbounds"] = new JArray
                    {
                        new JObject
                        {
                            ["protocol"] = "tuic",
                            ["settings"] = new JObject
                            {
                                ["server"] = $"{host}:{portStr}",
                                ["uuid"] = uuid,
                                ["password"] = paramsDict.GetValueOrDefault("password", ""),
                                ["congestionControl"] = paramsDict.GetValueOrDefault("congestion_control", "bbr"),
                                ["udpRelayMode"] = paramsDict.GetValueOrDefault("udp_relay_mode", "native")
                            },
                            ["streamSettings"] = new JObject
                            {
                                ["security"] = "tls",
                                ["tlsSettings"] = new JObject
                                {
                                    ["serverName"] = paramsDict.GetValueOrDefault("sni", host)
                                }
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"TUIC config error: {ex.Message}");
                return null;
            }
        }

        private static JObject BuildSingboxConfig(string uri, ServerInfo server, ConnectionMode connectionMode,
            string domainStrategy = "prefer_ipv4", string listenAddr = "127.0.0.1",
            bool httpEnabled = true, bool socksEnabled = true, bool muxEnabled = true, bool gameMode = false)
        {
            try
            {
                var allOutbounds = new JArray();
                bool builtMulti = false;

                if (!string.IsNullOrEmpty(server?.RawJsonConfig))
                {
                    try
                    {
                        var rawDoc = JObject.Parse(server.RawJsonConfig);
                        if (rawDoc["outbounds"] is JArray rawObs)
                        {
                            var proxyObs = rawObs.OfType<JObject>().Where(SubscriptionManager.IsProxyOutbound).ToList();
                            if (proxyObs.Count > 1)
                            {
                                var nodeTags = new JArray();
                                int idx = 0;
                                foreach (var ob in proxyObs)
                                {
                                    string subUri = SubscriptionManager.OutboundJsonToUri(ob, $"node-{idx}");
                                    if (string.IsNullOrEmpty(subUri)) continue;
                                    var sbOb = BuildSingboxOutboundFromUri(subUri, domainStrategy, muxEnabled);
                                    if (sbOb == null) continue;
                                    string tag = $"node-{idx++}";
                                    sbOb["tag"] = tag;
                                    allOutbounds.Add(sbOb);
                                    nodeTags.Add(tag);
                                }
                                if (nodeTags.Count >= 2)
                                {
                                    allOutbounds.Insert(0, new JObject
                                    {
                                        ["type"] = "urltest",
                                        ["tag"] = "proxy",
                                        ["outbounds"] = nodeTags,
                                        ["url"] = "http://www.gstatic.com/generate_204",
                                        ["interval"] = "3m",
                                        ["tolerance"] = 50
                                    });
                                    builtMulti = true;
                                }
                                else if (nodeTags.Count == 1)
                                {
                                    allOutbounds[0]["tag"] = "proxy";
                                    builtMulti = true;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (!builtMulti)
                {
                    allOutbounds.Clear();
                    var singleOb = BuildSingboxOutboundFromUri(uri, domainStrategy, muxEnabled);
                    if (singleOb == null)
                    {
                        Logger.Error($"Unsupported protocol for Sing-box: {uri.Substring(0, Math.Min(30, uri.Length))}");
                        return null;
                    }
                    allOutbounds.Add(singleOb);
                }

                allOutbounds.Add(new JObject { ["type"] = "direct", ["tag"] = "direct" });
                allOutbounds.Add(new JObject { ["type"] = "block", ["tag"] = "block" });

                foreach (var ob in allOutbounds.OfType<JObject>())
                {
                    if (ob["domain_resolver"] != null)
                        ob["domain_resolver"] = new JObject { ["server"] = "dns-direct", ["strategy"] = domainStrategy };
                }

                var inbounds = new JArray();
                bool hasAuth = Config.ProxyAuthEnabled &&
                               !string.IsNullOrWhiteSpace(Config.ProxyAuthUser) &&
                               !string.IsNullOrWhiteSpace(Config.ProxyAuthPassword);

                if (socksEnabled)
                {
                    var socksIn = new JObject { ["type"] = "socks", ["tag"] = "socks-in", ["listen"] = listenAddr, ["listen_port"] = Config.SocksPort };
                    if (hasAuth)
                    {
                        socksIn["users"] = new JArray
                        {
                            new JObject { ["username"] = Config.ProxyAuthUser, ["password"] = Config.ProxyAuthPassword }
                        };
                    }
                    inbounds.Add(socksIn);
                }
                if (httpEnabled || inbounds.Count == 0)
                {
                    var httpIn = new JObject { ["type"] = "http", ["tag"] = "http-in", ["listen"] = listenAddr, ["listen_port"] = Config.ProxyPort };
                    if (hasAuth && Config.HttpAuthOnHotspot && Config.AllowLan)
                    {
                        httpIn["users"] = new JArray
                        {
                            new JObject { ["username"] = Config.ProxyAuthUser, ["password"] = Config.ProxyAuthPassword }
                        };
                    }
                    inbounds.Add(httpIn);
                }

                var ruDomains = Config.RuDomains.Select(d => d.Replace("domain:", "")).ToList();
                string remoteDns = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns.Trim();

                if (connectionMode == ConnectionMode.Tun)
                {
                    var tunAddr = domainStrategy == "prefer_ipv6"
                        ? new JArray(Config.TunInet4Address, Config.TunInet6Address)
                        : new JArray(Config.TunInet4Address);

                    var tunInbound = new JObject
                    {
                        ["type"] = "tun",
                        ["tag"] = "tun-in",
                        ["address"] = tunAddr,
                        ["auto_route"] = true,
                        ["strict_route"] = false,
                        ["stack"] = "mixed",
                        ["mtu"] = Config.TunMtu
                    };
                    var bypassCidrs = ResolveBypassCidrs(server);
                    if (bypassCidrs.Count > 0)
                        tunInbound["route_exclude_address"] = new JArray(bypassCidrs);

                    inbounds.Insert(0, tunInbound);
                }

                var dnsServers = new JArray
                {
                    new JObject { ["type"] = "local", ["tag"] = "dns-direct" },
                    new JObject { ["type"] = "udp", ["tag"] = "dns-remote", ["server"] = remoteDns, ["detour"] = "proxy" }
                };

                var dnsRules = new JArray();
                if (Config.BypassRu)
                    dnsRules.Add(new JObject { ["domain_suffix"] = new JArray(ruDomains), ["server"] = "dns-direct" });

                var routeRules = new JArray
                {
                    new JObject { ["action"] = "sniff" },
                    new JObject { ["protocol"] = "dns", ["action"] = "hijack-dns" }
                };
                if (Config.BlockUdp)
                    routeRules.Add(new JObject { ["network"] = "udp", ["action"] = "reject" });
                if (Config.BypassLan)
                {
                    routeRules.Add(new JObject { ["ip_is_private"] = true, ["action"] = "route", ["outbound"] = "direct" });
                    routeRules.Add(new JObject { ["ip_cidr"] = new JArray("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128", "fc00::/7"), ["action"] = "route", ["outbound"] = "direct" });
                }
                if (Config.BypassRu)
                    routeRules.Add(new JObject { ["domain_suffix"] = new JArray(ruDomains), ["action"] = "route", ["outbound"] = "direct" });

                string finalOutbound = "proxy";
                if (connectionMode == ConnectionMode.Tun)
                    ApplyProcessRoutingRules(routeRules, out finalOutbound);

                return new JObject
                {
                    ["log"] = new JObject { ["level"] = gameMode ? "fatal" : "warn" },
                    ["dns"] = new JObject
                    {
                        ["servers"] = dnsServers,
                        ["rules"] = dnsRules,
                        ["final"] = connectionMode == ConnectionMode.Tun ? "dns-remote" : "dns-direct",
                        ["strategy"] = domainStrategy == "prefer_ipv6" ? "prefer_ipv6" : "ipv4_only"
                    },
                    ["inbounds"] = inbounds,
                    ["outbounds"] = allOutbounds,
                    ["route"] = new JObject
                    {
                        ["auto_detect_interface"] = true,
                        ["rules"] = routeRules,
                        ["final"] = finalOutbound,
                        ["default_domain_resolver"] = new JObject { ["server"] = "dns-direct", ["strategy"] = domainStrategy }
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"Sing-box config error: {ex.Message}");
                return null;
            }
        }

        public static JObject BuildXrayTunBridgeConfig(ServerInfo server, string preferredIp = "auto", bool gameMode = false)
        {
            string domainStrategy = preferredIp == "ipv6" ? "prefer_ipv6" : "prefer_ipv4";
            var ruDomains = Config.RuDomains.Select(d => d.Replace("domain:", "")).ToList();
            var tunAddr = domainStrategy == "prefer_ipv6"
                ? new JArray(Config.TunInet4Address, Config.TunInet6Address)
                : new JArray(Config.TunInet4Address);

            var tunInbound = new JObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["address"] = tunAddr,
                ["auto_route"] = true,
                ["strict_route"] = false,
                ["stack"] = "mixed",
                ["mtu"] = Config.TunMtu
            };
            var bypassCidrs = ResolveBypassCidrs(server);
            if (bypassCidrs.Count > 0)
                tunInbound["route_exclude_address"] = new JArray(bypassCidrs);

            string remoteDns = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns.Trim();
            bool hasAuth = Config.ProxyAuthEnabled &&
                           !string.IsNullOrWhiteSpace(Config.ProxyAuthUser) &&
                           !string.IsNullOrWhiteSpace(Config.ProxyAuthPassword);

            var socksOut = new JObject
            {
                ["type"] = "socks",
                ["tag"] = "proxy",
                ["server"] = "127.0.0.1",
                ["server_port"] = Config.SocksPort,
                ["version"] = "5"
            };
            if (hasAuth)
            {
                socksOut["username"] = Config.ProxyAuthUser;
                socksOut["password"] = Config.ProxyAuthPassword;
            }

            var dnsRules = new JArray();
            if (Config.BypassRu)
                dnsRules.Add(new JObject { ["domain_suffix"] = new JArray(ruDomains), ["server"] = "dns-direct" });

            var routeRules = new JArray
            {
                new JObject { ["action"] = "sniff" },
                new JObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                new JObject { ["process_name"] = new JArray("xray.exe"), ["action"] = "route", ["outbound"] = "direct" }
            };
            if (Config.BlockUdp)
                routeRules.Add(new JObject { ["network"] = "udp", ["action"] = "reject" });
            if (Config.BypassLan)
            {
                routeRules.Add(new JObject { ["ip_is_private"] = true, ["action"] = "route", ["outbound"] = "direct" });
                routeRules.Add(new JObject { ["ip_cidr"] = new JArray("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128", "fc00::/7"), ["action"] = "route", ["outbound"] = "direct" });
            }
            if (Config.BypassRu)
                routeRules.Add(new JObject { ["domain_suffix"] = new JArray(ruDomains), ["action"] = "route", ["outbound"] = "direct" });

            ApplyProcessRoutingRules(routeRules, out string finalOutbound);

            return new JObject
            {
                ["log"] = new JObject { ["level"] = gameMode ? "fatal" : "warn" },
                ["dns"] = new JObject
                {
                    ["servers"] = new JArray
                    {
                        new JObject { ["type"] = "local", ["tag"] = "dns-direct" },
                        new JObject { ["type"] = "udp", ["tag"] = "dns-remote", ["server"] = remoteDns, ["detour"] = "proxy" }
                    },
                    ["rules"] = dnsRules,
                    ["final"] = "dns-remote",
                    ["strategy"] = domainStrategy == "prefer_ipv6" ? "prefer_ipv6" : "ipv4_only"
                },
                ["inbounds"] = new JArray { tunInbound },
                ["outbounds"] = new JArray
                {
                    socksOut,
                    new JObject { ["type"] = "direct", ["tag"] = "direct" },
                    new JObject { ["type"] = "block", ["tag"] = "block" }
                },
                ["route"] = new JObject
                {
                    ["auto_detect_interface"] = true,
                    ["rules"] = routeRules,
                    ["final"] = finalOutbound,
                    ["default_domain_resolver"] = new JObject { ["server"] = "dns-direct", ["strategy"] = domainStrategy }
                }
            };
        }

        private static void ApplyProcessRoutingRules(JArray routeRules, out string finalOutbound)
        {
            finalOutbound = "proxy";
            if (!Config.ProcessRulesEnabled || Config.ProcessRulesList == null || Config.ProcessRulesList.Count == 0)
                return;

            var procs = Config.ProcessRulesList
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Where(p => !p.Equals("xray.exe", StringComparison.OrdinalIgnoreCase)
                         && !p.Equals("sing-box.exe", StringComparison.OrdinalIgnoreCase)
                         && !p.Equals("hivox.exe", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (procs.Count == 0) return;

            if (string.Equals(Config.ProcessRulesMode, "only_vpn", StringComparison.OrdinalIgnoreCase))
            {
                routeRules.Add(new JObject
                {
                    ["process_name"] = new JArray(procs),
                    ["action"] = "route",
                    ["outbound"] = "proxy"
                });
                finalOutbound = "direct";
            }
            else
            {
                routeRules.Add(new JObject
                {
                    ["process_name"] = new JArray(procs),
                    ["action"] = "route",
                    ["outbound"] = "direct"
                });
            }
        }

        private static List<string> ResolveBypassCidrs(ServerInfo server)
        {
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(server?.Host))
                hosts.Add(server.Host.Trim());

            if (!string.IsNullOrWhiteSpace(server?.RawJsonConfig))
            {
                try
                {
                    var doc = JObject.Parse(server.RawJsonConfig);
                    if (doc["outbounds"] is JArray obs)
                    {
                        foreach (var ob in obs.OfType<JObject>())
                        {
                            string s = ob["server"]?.ToString()
                                ?? ob["settings"]?["vnext"]?[0]?["address"]?.ToString()
                                ?? ob["settings"]?["servers"]?[0]?["address"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(s))
                                hosts.Add(s.Trim());
                        }
                    }
                }
                catch { }
            }

            var cidrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in hosts)
            {
                try
                {
                    if (IPAddress.TryParse(h.Trim('[', ']'), out var literal))
                    {
                        if (!IPAddress.IsLoopback(literal))
                            cidrs.Add(literal.AddressFamily == AddressFamily.InterNetwork ? $"{literal}/32" : $"{literal}/128");
                        continue;
                    }
                    foreach (var ip in Dns.GetHostAddresses(h))
                    {
                        if (!IPAddress.IsLoopback(ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                            cidrs.Add($"{ip}/32");
                    }
                }
                catch { }
            }
            return cidrs.ToList();
        }

        public static JObject BuildXrayOutboundFromUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return null;
            string lower = uri.Trim().ToLowerInvariant();
            JObject cfg = null;
            if (lower.StartsWith("vmess://")) cfg = BuildVmessConfig(uri);
            else if (lower.StartsWith("vless://")) cfg = BuildVlessConfig(uri);
            else if (lower.StartsWith("trojan://")) cfg = BuildTrojanConfig(uri);
            else if (lower.StartsWith("ss://")) cfg = BuildShadowsocksConfig(uri);
            else if (lower.StartsWith("socks://") || lower.StartsWith("socks5://")) cfg = BuildSocksConfig(uri);
            return cfg?["outbounds"]?[0] as JObject;
        }

        public static JObject BuildSingboxOutboundFromUri(string uri, string domainStrategy = "prefer_ipv4", bool muxEnabled = false)
        {
            try
            {
                JObject outbound = null;
                string uriClean = uri.Split('#')[0];
                string uriLower = uriClean.ToLowerInvariant();

                if (uriLower.StartsWith("vmess://"))
                {
                    var data = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(FixBase64(uriClean.Substring(8)))));
                    string vmessSni = data["sni"]?.ToString();
                    if (string.IsNullOrEmpty(vmessSni)) vmessSni = data["host"]?.ToString();
                    if (string.IsNullOrEmpty(vmessSni)) vmessSni = data["add"]?.ToString() ?? "";
                    outbound = new JObject
                    {
                        ["type"] = "vmess",
                        ["tag"] = "proxy",
                        ["server"] = data["add"]?.ToString() ?? "",
                        ["server_port"] = int.TryParse(data["port"]?.ToString(), out int vp) ? vp : 443,
                        ["uuid"] = data["id"]?.ToString() ?? "",
                        ["alter_id"] = int.TryParse(data["aid"]?.ToString(), out int vaid) ? vaid : 0,
                        ["security"] = data["scy"]?.ToString() ?? "auto",
                        ["tls"] = new JObject
                        {
                            ["enabled"] = data["tls"]?.ToString() is string t && (t == "1" || t.Equals("tls", StringComparison.OrdinalIgnoreCase) || t.Equals("true", StringComparison.OrdinalIgnoreCase)),
                            ["server_name"] = vmessSni
                        }
                    };
                    string net = (data["net"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(net) && net != "tcp")
                    {
                        var transport = new JObject { ["type"] = net is "h2" or "xhttp" ? "http" : net };
                        if (net is "ws" or "http" or "h2" or "xhttp")
                            transport["path"] = data["path"]?.ToString() ?? "/";
                        if (net == "grpc")
                            transport["service_name"] = data["path"]?.ToString() ?? "";
                        if (net is "http" or "h2" or "xhttp")
                        {
                            string vmessHost = data["host"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(vmessHost))
                                transport["host"] = new JArray(vmessHost);
                        }
                        if (net == "ws")
                        {
                            string vmessHost = data["host"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(vmessHost))
                                transport["headers"] = new JObject { ["Host"] = vmessHost };
                        }
                        outbound["transport"] = transport;
                    }
                }
                else if (uriLower.StartsWith("vless://"))
                {
                    string body = uriClean.Substring(8);
                    int atIdx = body.IndexOf('@');
                    string uuidPart = SafeUnescape(body[..atIdx]);
                    string restPart = body[(atIdx + 1)..];
                    string hp = restPart.Split('?')[0];
                    var (host, port) = ParseHostPort(hp);
                    var sbParams = ParseUriParams(restPart.Contains('?') ? restPart.Split('?', 2)[1] : "");
                    string sec = sbParams.GetValueOrDefault("security", "none").ToLowerInvariant();
                    bool hasTls = sec is "tls" or "reality";
                    string sni = sbParams.GetValueOrDefault("sni", "");
                    if (string.IsNullOrEmpty(sni)) sni = host;
                    var tlsObj = new JObject
                    {
                        ["enabled"] = hasTls,
                        ["server_name"] = sni
                    };
                    if (IsInsecureParam(sbParams))
                        tlsObj["insecure"] = true;
                    if (sbParams.ContainsKey("alpn") && !string.IsNullOrWhiteSpace(sbParams["alpn"]))
                        tlsObj["alpn"] = new JArray(sbParams["alpn"].Split(',').Select(x => x.Trim()));
                    if (sbParams.ContainsKey("fp") && !string.IsNullOrWhiteSpace(sbParams["fp"]))
                        tlsObj["utls"] = new JObject { ["enabled"] = true, ["fingerprint"] = sbParams["fp"] };

                    outbound = new JObject
                    {
                        ["type"] = "vless",
                        ["tag"] = "proxy",
                        ["server"] = host,
                        ["server_port"] = port,
                        ["uuid"] = uuidPart,
                        ["tls"] = tlsObj
                    };
                    if (sbParams.ContainsKey("flow") && !string.IsNullOrEmpty(sbParams["flow"]))
                        outbound["flow"] = sbParams["flow"];
                    string trType = sbParams.GetValueOrDefault("type", "").ToLowerInvariant();
                    if (trType == "ws")
                    {
                        var transport = new JObject { ["type"] = "ws", ["path"] = sbParams.GetValueOrDefault("path", "/") };
                        if (sbParams.ContainsKey("host") && !string.IsNullOrEmpty(sbParams["host"]))
                            transport["headers"] = new JObject { ["Host"] = sbParams["host"] };
                        outbound["transport"] = transport;
                    }
                    else if (trType == "grpc")
                        outbound["transport"] = new JObject { ["type"] = "grpc", ["service_name"] = sbParams.GetValueOrDefault("serviceName", sbParams.GetValueOrDefault("path", "")) };
                    else if (trType is "h2" or "http" or "xhttp")
                    {
                        var transport = new JObject { ["type"] = "http", ["path"] = sbParams.GetValueOrDefault("path", "/") };
                        if (sbParams.ContainsKey("host") && !string.IsNullOrEmpty(sbParams["host"]))
                            transport["host"] = new JArray(sbParams["host"]);
                        outbound["transport"] = transport;
                    }
                    if (sec == "reality")
                    {
                        tlsObj["reality"] = new JObject
                        {
                            ["enabled"] = true,
                            ["public_key"] = sbParams.GetValueOrDefault("pbk", ""),
                            ["short_id"] = sbParams.GetValueOrDefault("sid", "")
                        };
                        if (tlsObj["utls"] == null)
                        {
                            tlsObj["utls"] = new JObject
                            {
                                ["enabled"] = true,
                                ["fingerprint"] = sbParams.GetValueOrDefault("fp", "chrome")
                            };
                        }
                    }
                }
                else if (uriLower.StartsWith("hysteria2://") || uriLower.StartsWith("hy2://"))
                {
                    string rest = uriClean.Split(new[] { "://" }, 2, StringSplitOptions.None)[1];
                    string auth = "";
                    if (rest.Contains('@'))
                    {
                        int atIdx = rest.LastIndexOf('@');
                        auth = SafeUnescape(rest[..atIdx]);
                        rest = rest[(atIdx + 1)..];
                    }
                    string hp = rest.Split('?')[0];
                    var (host, port) = ParseHostPort(hp);
                    var hyParams = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");
                    string sni = hyParams.GetValueOrDefault("sni", "");
                    if (string.IsNullOrEmpty(sni)) sni = host;
                    var tlsObj = new JObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = sni
                    };
                    if (IsInsecureParam(hyParams))
                        tlsObj["insecure"] = true;
                    if (hyParams.ContainsKey("alpn") && !string.IsNullOrWhiteSpace(hyParams["alpn"]))
                        tlsObj["alpn"] = new JArray(hyParams["alpn"].Split(',').Select(x => x.Trim()));

                    outbound = new JObject
                    {
                        ["type"] = "hysteria2",
                        ["tag"] = "proxy",
                        ["server"] = host,
                        ["server_port"] = port,
                        ["password"] = auth,
                        ["tls"] = tlsObj
                    };
                    if (hyParams.ContainsKey("down_mbps") && int.TryParse(hyParams["down_mbps"], out int down))
                        outbound["down_mbps"] = down;
                    if (hyParams.ContainsKey("up_mbps") && int.TryParse(hyParams["up_mbps"], out int up))
                        outbound["up_mbps"] = up;
                    if (hyParams.ContainsKey("obfs") && !string.IsNullOrEmpty(hyParams["obfs"]))
                    {
                        var obfsConfig = new JObject { ["type"] = hyParams["obfs"] };
                        if (hyParams.ContainsKey("obfs-password"))
                            obfsConfig["password"] = hyParams["obfs-password"];
                        outbound["obfs"] = obfsConfig;
                    }
                }
                else if (uriLower.StartsWith("hysteria://"))
                {
                    string rest = uriClean.Substring(11);
                    string hp = rest.Split('?')[0];
                    if (hp.Contains('@')) hp = hp.Split('@')[^1];
                    var (host, port) = ParseHostPort(hp);
                    var hyParams = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");
                    string sni = hyParams.GetValueOrDefault("peer", hyParams.GetValueOrDefault("sni", host));
                    var tlsObj = new JObject { ["enabled"] = true, ["server_name"] = string.IsNullOrEmpty(sni) ? host : sni };
                    if (IsInsecureParam(hyParams)) tlsObj["insecure"] = true;
                    outbound = new JObject
                    {
                        ["type"] = "hysteria",
                        ["tag"] = "proxy",
                        ["server"] = host,
                        ["server_port"] = port,
                        ["auth_str"] = hyParams.GetValueOrDefault("auth", ""),
                        ["up_mbps"] = int.TryParse(hyParams.GetValueOrDefault("upmbps", "20"), out int u) ? u : 20,
                        ["down_mbps"] = int.TryParse(hyParams.GetValueOrDefault("downmbps", "100"), out int d) ? d : 100,
                        ["tls"] = tlsObj
                    };
                }
                else if (uriLower.StartsWith("trojan://"))
                {
                    string body = uriClean.Substring(9);
                    int atIdx = body.LastIndexOf('@');
                    string pwPart = SafeUnescape(body[..atIdx]);
                    string restPart = body[(atIdx + 1)..];
                    string hp = restPart.Split('?')[0];
                    var (host, port) = ParseHostPort(hp);
                    var trParams = ParseUriParams(restPart.Contains('?') ? restPart.Split('?', 2)[1] : "");
                    string sni = trParams.GetValueOrDefault("sni", "");
                    if (string.IsNullOrEmpty(sni)) sni = host;
                    var tlsObj = new JObject
                    {
                        ["enabled"] = trParams.GetValueOrDefault("security", "tls") != "none",
                        ["server_name"] = sni
                    };
                    if (IsInsecureParam(trParams)) tlsObj["insecure"] = true;
                    if (trParams.ContainsKey("fp") && !string.IsNullOrWhiteSpace(trParams["fp"]))
                        tlsObj["utls"] = new JObject { ["enabled"] = true, ["fingerprint"] = trParams["fp"] };

                    outbound = new JObject
                    {
                        ["type"] = "trojan",
                        ["tag"] = "proxy",
                        ["server"] = host,
                        ["server_port"] = port,
                        ["password"] = pwPart,
                        ["tls"] = tlsObj
                    };
                    string trType = trParams.GetValueOrDefault("type", "").ToLowerInvariant();
                    if (trType == "ws")
                    {
                        var transport = new JObject { ["type"] = "ws", ["path"] = trParams.GetValueOrDefault("path", "/") };
                        if (trParams.ContainsKey("host")) transport["headers"] = new JObject { ["Host"] = trParams["host"] };
                        outbound["transport"] = transport;
                    }
                    else if (trType == "grpc")
                        outbound["transport"] = new JObject { ["type"] = "grpc", ["service_name"] = trParams.GetValueOrDefault("serviceName", trParams.GetValueOrDefault("path", "")) };
                }
                else if (uriLower.StartsWith("ss://"))
                {
                    try
                    {
                        var (method, password, host, port) = ParseShadowsocksUri(uriClean);
                        outbound = new JObject
                        {
                            ["type"] = "shadowsocks",
                            ["tag"] = "proxy",
                            ["server"] = host,
                            ["server_port"] = port,
                            ["method"] = method,
                            ["password"] = password
                        };
                    }
                    catch { }
                }
                else if (uriLower.StartsWith("socks://") || uriLower.StartsWith("socks5://"))
                {
                    int sepIdx = uriClean.IndexOf("://", StringComparison.Ordinal);
                    string rest = uriClean[(sepIdx + 3)..];
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
                    outbound = new JObject
                    {
                        ["type"] = "socks",
                        ["tag"] = "proxy",
                        ["server"] = host,
                        ["server_port"] = port
                    };
                    if (!string.IsNullOrEmpty(user)) outbound["username"] = user;
                    if (!string.IsNullOrEmpty(pass)) outbound["password"] = pass;
                }
                else if (uriLower.StartsWith("anytls://"))
                {
                    string rest = uriClean.Substring(9);
                    string pw = "";
                    if (rest.Contains('@'))
                    {
                        int atIdx = rest.LastIndexOf('@');
                        pw = SafeUnescape(rest[..atIdx]);
                        rest = rest[(atIdx + 1)..];
                    }
                    string hp = rest.Split('?')[0];
                    var (host, port) = ParseHostPort(hp);
                    var p = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");
                    string sni = p.GetValueOrDefault("sni", host);
                    var tlsObj = new JObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = string.IsNullOrEmpty(sni) ? host : sni
                    };
                    if (IsInsecureParam(p)) tlsObj["insecure"] = true;
                    outbound = new JObject
                    {
                        ["type"] = "anytls",
                        ["tag"] = "proxy",
                        ["server"] = host,
                        ["server_port"] = port,
                        ["password"] = pw,
                        ["tls"] = tlsObj
                    };
                }
                else if (uriLower.StartsWith("tuic://"))
                {
                    string rest = uriClean.Split(new[] { "://" }, 2, StringSplitOptions.None)[1];
                    string uuidPart = "";
                    if (rest.Contains('@'))
                    {
                        int atIdx = rest.LastIndexOf('@');
                        uuidPart = SafeUnescape(rest[..atIdx]);
                        rest = rest[(atIdx + 1)..];
                    }
                    string hp = rest.Split('?')[0];
                    var (host, port) = ParseHostPort(hp);
                    var tuicParams = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");
                    string sni = tuicParams.GetValueOrDefault("sni", "");
                    if (string.IsNullOrEmpty(sni)) sni = host;
                    var tlsObj = new JObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = sni
                    };
                    if (IsInsecureParam(tuicParams)) tlsObj["insecure"] = true;
                    if (tuicParams.ContainsKey("alpn") && !string.IsNullOrWhiteSpace(tuicParams["alpn"]))
                        tlsObj["alpn"] = new JArray(tuicParams["alpn"].Split(',').Select(x => x.Trim()));

                    outbound = new JObject
                    {
                        ["type"] = "tuic",
                        ["tag"] = "proxy",
                        ["server"] = host,
                        ["server_port"] = port,
                        ["uuid"] = uuidPart,
                        ["password"] = tuicParams.GetValueOrDefault("password", ""),
                        ["congestion_control"] = tuicParams.GetValueOrDefault("congestion_control", "bbr"),
                        ["udp_relay_mode"] = tuicParams.GetValueOrDefault("udp_relay_mode", "native"),
                        ["tls"] = tlsObj
                    };
                }

                if (outbound == null || string.IsNullOrEmpty(outbound["server"]?.ToString()))
                    return null;

                outbound["domain_resolver"] = new JObject { ["server"] = "google", ["strategy"] = domainStrategy };

                if (muxEnabled && outbound["type"]?.ToString() is "vmess" or "vless" && outbound["flow"] == null)
                    outbound["multiplex"] = new JObject { ["enabled"] = true, ["protocol"] = "h2mux" };

                return outbound;
            }
            catch (Exception ex)
            {
                Logger.Error($"Sing-box outbound parse error: {ex.Message}");
                return null;
            }
        }

        private static JObject BuildVmessConfig(string uri)
        {
            try
            {
                var data = JObject.Parse(Encoding.UTF8.GetString(
                    Convert.FromBase64String(FixBase64(uri.Substring(8)))));
                string net = (data["net"]?.ToString() ?? "tcp").Trim().ToLower();
                string tlsVal = data["tls"]?.ToString() ?? "";
                string security = tlsVal == "1" || tlsVal.Equals("tls", System.StringComparison.OrdinalIgnoreCase) || tlsVal.Equals("true", System.StringComparison.OrdinalIgnoreCase) ? "tls" : "none";
                var streamSettings = new JObject { ["network"] = net, ["security"] = security };

                if (net == "ws")
                {
                    var wsSettings = new JObject { ["path"] = data["path"]?.ToString() ?? "/" };
                    string hostHdr = data["host"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(hostHdr))
                        wsSettings["headers"] = new JObject { ["Host"] = hostHdr };
                    streamSettings["wsSettings"] = wsSettings;
                }
                else if (net == "grpc")
                    streamSettings["grpcSettings"] = new JObject { ["serviceName"] = data["path"]?.ToString() ?? "" };
                else if (net is "h2" or "http")
                {
                    var httpSettings = new JObject { ["path"] = data["path"]?.ToString() ?? "/" };
                    string httpHost = data["host"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(httpHost))
                        httpSettings["host"] = new JArray(httpHost);
                    streamSettings["httpSettings"] = httpSettings;
                }
                else if (net == "xhttp")
                {
                    var xhSettings = new JObject
                    {
                        ["mode"] = data["mode"]?.ToString() ?? "auto",
                        ["path"] = data["path"]?.ToString() ?? "/"
                    };
                    string hostHdr = data["host"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(hostHdr))
                        xhSettings["host"] = hostHdr;
                    string extraRaw = data["extra"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(extraRaw))
                    {
                        try
                        {
                            var extra = JObject.Parse(extraRaw);
                            foreach (var prop in extra.Properties())
                                xhSettings[prop.Name] = prop.Value;
                        }
                        catch { }
                    }
                    streamSettings["xhttpSettings"] = xhSettings;
                }

                if (security == "tls")
                {
                    streamSettings["tlsSettings"] = new JObject
                    {
                        ["serverName"] = data["sni"]?.ToString() ?? data["host"]?.ToString() ?? data["add"]?.ToString() ?? "",
                        ["alpn"] = new JArray("h2", "http/1.1")
                    };
                }

                return new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "warning" },
                    ["inbounds"] = new JArray
                    {
                        new JObject { ["port"] = Config.SocksPort, ["protocol"] = "socks", ["settings"] = new JObject { ["udp"] = true }, ["sniffing"] = new JObject { ["enabled"] = true, ["destOverride"] = new JArray("http", "tls") } },
                        new JObject { ["port"] = Config.ProxyPort, ["protocol"] = "http" }
                    },
                    ["outbounds"] = new JArray
                    {
                        new JObject
                        {
                            ["protocol"] = "vmess",
                            ["settings"] = new JObject
                            {
                                ["vnext"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["address"] = data["add"]?.ToString() ?? "",
                                        ["port"] = int.Parse(data["port"]?.ToString() ?? "443"),
                                        ["users"] = new JArray
                                        {
                                            new JObject
                                            {
                                                ["id"] = data["id"]?.ToString() ?? "",
                                                ["alterId"] = int.Parse(data["aid"]?.ToString() ?? "0"),
                                                ["security"] = data["scy"]?.ToString() ?? "auto"
                                            }
                                        }
                                    }
                                }
                            },
                            ["streamSettings"] = streamSettings
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"VMess config error: {ex.Message}");
                return null;
            }
        }

        private static JObject BuildVlessConfig(string uri)
        {
            string uriClean = uri.Substring(8).Split('#')[0];
            string[] parts = uriClean.Split('@', 2);
            string uuid = SafeUnescape(parts[0]);
            string rest = parts[1];
            string hp = rest.Split('?')[0];
            var (host, port) = ParseHostPort(hp);
            var paramsDict = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");
            string transportType = paramsDict.GetValueOrDefault("type", "tcp").ToLowerInvariant();
            string sec = paramsDict.GetValueOrDefault("security", "none").ToLowerInvariant();
            var streamSettings = new JObject
            {
                ["network"] = transportType,
                ["security"] = sec
            };

            if (transportType == "ws")
            {
                var wsSettings = new JObject { ["path"] = paramsDict.GetValueOrDefault("path", "/") };
                if (paramsDict.ContainsKey("host"))
                    wsSettings["headers"] = new JObject { ["Host"] = paramsDict["host"] };
                streamSettings["wsSettings"] = wsSettings;
            }
            else if (transportType == "grpc")
                streamSettings["grpcSettings"] = new JObject { ["serviceName"] = paramsDict.GetValueOrDefault("serviceName", paramsDict.GetValueOrDefault("path", "")) };
            else if (transportType is "h2" or "http")
            {
                var httpSettings = new JObject { ["path"] = paramsDict.GetValueOrDefault("path", "/") };
                if (paramsDict.ContainsKey("host"))
                    httpSettings["host"] = new JArray(paramsDict["host"]);
                streamSettings["httpSettings"] = httpSettings;
            }
            else if (transportType == "xhttp")
            {
                var xhSettings = new JObject
                {
                    ["mode"] = paramsDict.GetValueOrDefault("mode", "auto"),
                    ["path"] = paramsDict.GetValueOrDefault("path", "/")
                };
                if (paramsDict.ContainsKey("host"))
                    xhSettings["host"] = paramsDict["host"];
                string extraRaw = paramsDict.GetValueOrDefault("extra", "");
                if (!string.IsNullOrEmpty(extraRaw))
                {
                    try
                    {
                        var extra = JObject.Parse(extraRaw);
                        foreach (var prop in extra.Properties())
                            xhSettings[prop.Name] = prop.Value;
                    }
                    catch { }
                }
                streamSettings["xhttpSettings"] = xhSettings;
            }

            if (sec == "tls")
            {
                string sni = paramsDict.GetValueOrDefault("sni", "");
                if (string.IsNullOrEmpty(sni)) sni = host;
                var tls = new JObject
                {
                    ["serverName"] = sni,
                    ["alpn"] = new JArray(paramsDict.GetValueOrDefault("alpn", "h2,http/1.1").Split(',').Select(x => x.Trim()))
                };
                if (IsInsecureParam(paramsDict)) tls["allowInsecure"] = true;
                if (paramsDict.ContainsKey("fp") && !string.IsNullOrEmpty(paramsDict["fp"]))
                    tls["fingerprint"] = paramsDict["fp"];
                streamSettings["tlsSettings"] = tls;
            }
            if (sec == "reality")
            {
                var realitySettings = new JObject
                {
                    ["serverName"] = paramsDict.GetValueOrDefault("sni", ""),
                    ["fingerprint"] = paramsDict.GetValueOrDefault("fp", "chrome"),
                    ["publicKey"] = paramsDict.GetValueOrDefault("pbk", ""),
                    ["shortId"] = paramsDict.GetValueOrDefault("sid", ""),
                    ["multiMode"] = true
                };
                string spx = paramsDict.GetValueOrDefault("spiderX", paramsDict.GetValueOrDefault("spx", ""));
                if (!string.IsNullOrEmpty(spx))
                    realitySettings["spiderX"] = spx;
                streamSettings["realitySettings"] = realitySettings;
            }

            return new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "warning" },
                ["inbounds"] = new JArray
                {
                    new JObject { ["port"] = Config.SocksPort, ["protocol"] = "socks", ["settings"] = new JObject { ["udp"] = true }, ["sniffing"] = new JObject { ["enabled"] = true, ["destOverride"] = new JArray("http", "tls") } },
                    new JObject { ["port"] = Config.ProxyPort, ["protocol"] = "http" }
                },
                ["outbounds"] = new JArray
                {
                    new JObject
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
                                            ["encryption"] = string.IsNullOrEmpty(paramsDict.GetValueOrDefault("encryption", "")) ? "none" : paramsDict["encryption"],
                                            ["flow"] = paramsDict.GetValueOrDefault("flow", "")
                                        }
                                    }
                                }
                            }
                        },
                        ["streamSettings"] = streamSettings
                    }
                }
            };
        }

        private static JObject BuildTrojanConfig(string uri)
        {
            string uriClean = uri.Substring(9).Split('#')[0];
            int atIdx = uriClean.LastIndexOf('@');
            string password = SafeUnescape(uriClean[..atIdx]);
            string rest = uriClean[(atIdx + 1)..];
            string hp = rest.Split('?')[0];
            var (host, port) = ParseHostPort(hp);
            var paramsDict = ParseUriParams(rest.Contains('?') ? rest.Split('?', 2)[1] : "");
            return new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "warning" },
                ["inbounds"] = new JArray
                {
                    new JObject { ["port"] = Config.SocksPort, ["protocol"] = "socks", ["settings"] = new JObject { ["udp"] = true } },
                    new JObject { ["port"] = Config.ProxyPort, ["protocol"] = "http" }
                },
                ["outbounds"] = new JArray
                {
                    new JObject
                    {
                        ["protocol"] = "trojan",
                        ["settings"] = new JObject
                        {
                            ["servers"] = new JArray
                            {
                                new JObject { ["address"] = host, ["port"] = port, ["password"] = password }
                            }
                        },
                        ["streamSettings"] = BuildTrojanStreamSettings(host, paramsDict)
                    }
                }
            };
        }

        private static JObject BuildSocksConfig(string uri)
        {
            int sepIdx = uri.IndexOf("://", StringComparison.Ordinal);
            string uriClean = uri.Substring(sepIdx + 3).Split('#')[0];
            string user = "", pass = "";
            string hpPart = uriClean.Split('?')[0];
            if (hpPart.Contains('@'))
            {
                int atIdx = hpPart.LastIndexOf('@');
                string auth = hpPart[..atIdx];
                hpPart = hpPart[(atIdx + 1)..];
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
            var (host, port) = ParseHostPort(hpPart, 1080);
            var serverObj = new JObject { ["address"] = host, ["port"] = port };
            if (!string.IsNullOrEmpty(user))
            {
                serverObj["users"] = new JArray
                {
                    new JObject { ["user"] = user, ["pass"] = pass }
                };
            }
            return new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "warning" },
                ["inbounds"] = new JArray
                {
                    new JObject { ["port"] = Config.SocksPort, ["protocol"] = "socks", ["settings"] = new JObject { ["udp"] = true } },
                    new JObject { ["port"] = Config.ProxyPort, ["protocol"] = "http" }
                },
                ["outbounds"] = new JArray
                {
                    new JObject
                    {
                        ["protocol"] = "socks",
                        ["settings"] = new JObject { ["servers"] = new JArray { serverObj } }
                    }
                }
            };
        }

        private static JObject BuildShadowsocksConfig(string uri)
        {
            string uriClean = uri.Split('#')[0];
            try
            {
                var (method, password, host, port) = ParseShadowsocksUri(uriClean);
                return new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "warning" },
                    ["inbounds"] = new JArray
                    {
                        new JObject { ["port"] = Config.SocksPort, ["protocol"] = "socks", ["settings"] = new JObject { ["udp"] = true } },
                        new JObject { ["port"] = Config.ProxyPort, ["protocol"] = "http" }
                    },
                    ["outbounds"] = new JArray
                    {
                        new JObject
                        {
                            ["protocol"] = "shadowsocks",
                            ["settings"] = new JObject
                            {
                                ["servers"] = new JArray
                                {
                                    new JObject { ["address"] = host, ["port"] = port, ["method"] = method, ["password"] = password }
                                }
                            }
                        }
                    }
                };
            }
            catch { return null; }
        }

        private static bool IsValidIntOrRange(string raw, int minAllowed)
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
            else if (type == "rand" && !IsValidIntOrRange(packet, 1))
            {
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

        private static JObject ApplyOptimizations(JObject config, bool gameMode, bool muxEnabled, bool fragmentation = false, string preferredIp = "auto")
        {
            if (gameMode && config["log"] != null)
            {
                if (config["log"]["level"] != null)
                    config["log"]["level"] = "fatal";
                if (config["log"]["loglevel"] != null)
                    config["log"]["loglevel"] = "none";
            }

            bool isSingbox = config["outbounds"]?[0]?["type"] != null;

            if (!isSingbox)
            {
                string queryStrategy = preferredIp switch
                {
                    "ipv4" => "UseIPv4",
                    "ipv6" => "UseIPv6",
                    _ => "UseIP"
                };
                string primaryDns = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns.Trim();
                config["dns"] = new JObject
                {
                    ["servers"] = new JArray
                    {
                        new JObject { ["address"] = primaryDns, ["port"] = 53 },
                        new JObject { ["address"] = "8.8.8.8", ["port"] = 53 }
                    },
                    ["queryStrategy"] = queryStrategy
                };
            }

            foreach (var outbound in config["outbounds"] as JArray ?? new JArray())
            {
                string proto = outbound["protocol"]?.ToString() ?? outbound["type"]?.ToString() ?? "";
                if (proto is "freedom" or "blackhole" or "direct" or "block") continue;

                string transportNet = (outbound["streamSettings"]?["network"]?.ToString()) ??
                    (outbound["transport"]?["type"]?.ToString()) ?? "";

                if ((proto is "vmess" or "vless") && muxEnabled && transportNet != "xhttp")
                {
                    string flow = null;
                    try { flow = outbound["settings"]?["vnext"]?[0]?["users"]?[0]?["flow"]?.ToString(); } catch { }
                    if (string.IsNullOrEmpty(flow) && outbound["flow"] == null)
                    {
                        if (isSingbox)
                            outbound["multiplex"] = new JObject { ["enabled"] = true, ["protocol"] = "h2mux" };
                        else
                            outbound["mux"] = new JObject
                            {
                                ["enabled"] = true,
                                ["concurrency"] = Math.Clamp(Config.MuxConcurrency, 1, 64),
                                ["xudpConcurrency"] = Math.Clamp(Config.MuxXudpConcurrency, 1, 128),
                                ["xudpProxyUDP443"] = string.IsNullOrWhiteSpace(Config.MuxXudpQuic) ? "reject" : Config.MuxXudpQuic
                            };
                    }
                }

                if (proto is "vmess" or "vless" or "trojan" or "hysteria2" or "tuic")
                {
                    if (isSingbox)
                    {
                        outbound["tcp_fast_open"] = true;
                        outbound["tcp_no_delay"] = true;
                    }
                    else
                    {
                        var streamSettings = outbound["streamSettings"] as JObject ?? new JObject();
                        var sockopt = new JObject
                        {
                            ["tcpFastOpen"] = true,
                            ["tcpNoDelay"] = true,
                            ["tcpKeepAliveInterval"] = Config.TcpKeepaliveInterval,
                            ["tcpCongestion"] = "bbr"
                        };
                        streamSettings["sockopt"] = sockopt;
                        outbound["streamSettings"] = streamSettings;
                    }
                }
            }

            if ((fragmentation || Config.NoisesEnabled) && !isSingbox && config["outbounds"] is JArray outboundsArr)
            {
                if (outboundsArr[0]?["streamSettings"]?["sockopt"] is JObject firstSock)
                {
                    firstSock["dialerProxy"] = "fragment-out";
                }
                else if (outboundsArr[0] is JObject firstOb)
                {
                    var ss = firstOb["streamSettings"] as JObject ?? new JObject();
                    ss["sockopt"] = new JObject { ["dialerProxy"] = "fragment-out" };
                    firstOb["streamSettings"] = ss;
                }

                var fragSettings = new JObject();
                if (fragmentation)
                {
                    fragSettings["fragment"] = new JObject
                    {
                        ["packets"] = string.IsNullOrWhiteSpace(Config.FragmentPackets) ? "tlshello" : Config.FragmentPackets.Trim(),
                        ["length"] = IsValidIntOrRange(Config.FragmentLength, 1) ? Config.FragmentLength.Trim() : "100-200",
                        ["interval"] = IsValidIntOrRange(Config.FragmentInterval, 0) ? Config.FragmentInterval.Trim() : "10-20"
                    };
                }
                if (Config.NoisesEnabled)
                    fragSettings["noises"] = BuildNoisesArray();

                outboundsArr.Add(new JObject
                {
                    ["tag"] = "fragment-out",
                    ["protocol"] = "freedom",
                    ["settings"] = fragSettings
                });
            }
            return config;
        }

        private static JObject AddRussianBypass(JObject config)
        {
            if (config["outbounds"] != null)
            {
                config["outbounds"][0]["tag"] = "proxy";
                bool hasDirect = config["outbounds"].Any(o => o["tag"]?.ToString() == "direct");
                if (!hasDirect)
                {
                    bool isSingbox = config["outbounds"][0]["type"] != null;
                    if (isSingbox)
                        (config["outbounds"] as JArray).Add(new JObject { ["type"] = "direct", ["tag"] = "direct" });
                    else
                        (config["outbounds"] as JArray).Add(new JObject { ["protocol"] = "freedom", ["tag"] = "direct", ["settings"] = new JObject() });
                }

                var ruDomains = Config.RuDomains.Select(d => d.Replace("domain:", "")).ToList();
                var privateIps = new JArray("0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8",
                    "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24", "192.168.0.0/16",
                    "198.18.0.0/15", "224.0.0.0/4", "240.0.0.0/4");

                if (config["route"] != null)
                {
                    var rules = config["route"]["rules"] as JArray;
                    if (rules != null && Config.BypassRu)
                        rules.Insert(0, new JObject { ["domain"] = new JArray(ruDomains), ["outbound"] = "direct" });
                }
                else
                {
                    var xrayRules = new JArray();
                    if (Config.BlockUdp)
                    {
                        bool hasBlock = config["outbounds"].Any(o => o["tag"]?.ToString() == "block");
                        if (!hasBlock)
                            (config["outbounds"] as JArray).Add(new JObject { ["protocol"] = "blackhole", ["tag"] = "block", ["settings"] = new JObject() });
                        xrayRules.Add(new JObject { ["type"] = "field", ["network"] = "udp", ["outboundTag"] = "block" });
                    }
                    if (Config.BypassRu)
                        xrayRules.Add(new JObject { ["type"] = "field", ["domain"] = new JArray(Config.RuDomains), ["outboundTag"] = "direct" });
                    if (Config.BypassLan)
                        xrayRules.Add(new JObject { ["type"] = "field", ["ip"] = privateIps, ["outboundTag"] = "direct" });
                    xrayRules.Add(new JObject { ["type"] = "field", ["network"] = "tcp,udp", ["outboundTag"] = "proxy" });

                    config["routing"] = new JObject
                    {
                        ["domainStrategy"] = "IpIfNonMatch",
                        ["rules"] = xrayRules
                    };
                }
            }
            return config;
        }
    }
}
