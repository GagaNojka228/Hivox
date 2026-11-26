using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Hivox.Models;

namespace Hivox.Services
{
    public static class NetworkUtils
    {
        public static string PingMethod { get; set; } = "proxy";
        public static string PingTestUrl { get; set; } = "https://www.gstatic.com/generate_204";
        public static int PingTimeoutSec { get; set; } = 3;
        public static string AppDir { get; set; } = AppDomain.CurrentDomain.BaseDirectory;
        private const int ParallelPingLimit = 4;

        public static int PingHost(string host, int port = 443, int timeout = -1)
        {
            int effectiveTimeout = timeout > 0 ? timeout : Math.Clamp(PingTimeoutSec, 1, 15);
            return PingHostAsync(host, port, effectiveTimeout).GetAwaiter().GetResult();
        }

        public static async Task<int> PingHostAsync(string host, int port = 443, int timeout = -1, IPAddress resolvedIp = null)
        {
            if (string.IsNullOrWhiteSpace(host)) return -1;
            int targetPort = port > 0 ? port : 443;
            int effectiveTimeout = timeout > 0 ? timeout : Math.Clamp(PingTimeoutSec, 1, 15);

            if (string.Equals(PingMethod, "icmp", StringComparison.OrdinalIgnoreCase))
            {
                int icmp = PingIcmp(host, effectiveTimeout);
                if (icmp > 0) return icmp;
            }

            try
            {
                IPAddress targetIp = resolvedIp ?? await ResolveHostAsync(host, effectiveTimeout);
                if (targetIp == null) return -1;

                using var sock = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                sock.NoDelay = true;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));

                // Measure only the TCP handshake RTT (exclude DNS lookup and socket setup)
                var sw = Stopwatch.StartNew();
                await sock.ConnectAsync(new IPEndPoint(targetIp, targetPort), cts.Token);
                sw.Stop();
                return Math.Max(1, (int)sw.ElapsedMilliseconds);
            }
            catch { return -1; }
        }

        private static async Task<IPAddress> ResolveHostAsync(string host, int timeout)
        {
            if (IPAddress.TryParse(host.Trim('[', ']'), out var literalIp))
                return literalIp;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(2, timeout)));
                var addresses = await Dns.GetHostAddressesAsync(host, cts.Token);
                if (addresses == null || addresses.Length == 0) return null;
                return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
            }
            catch { return null; }
        }

        private static int PingIcmp(string host, int timeout)
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send(host, timeout * 1000);
                if (reply.Status == IPStatus.Success)
                    return Math.Max(1, (int)reply.RoundtripTime);
            }
            catch { }
            return -1;
        }

        public static int PingHostMulti(string host, int port = 443, int count = Config.PingMultiCount)
        {
            return PingHostMultiAsync(host, port, count).GetAwaiter().GetResult();
        }

        public static async Task<int> PingHostMultiAsync(string host, int port = 443, int count = Config.PingMultiCount, IPAddress resolvedIp = null)
        {
            if (string.IsNullOrWhiteSpace(host)) return -1;
            int timeoutSec = Math.Clamp(PingTimeoutSec, 1, 15);

            if (string.Equals(PingMethod, "icmp", StringComparison.OrdinalIgnoreCase))
            {
                var icmpPings = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int p = PingIcmp(host, timeoutSec);
                    if (p > 0) icmpPings.Add(p);
                    if (i + 1 < count) await Task.Delay(50);
                }
                if (icmpPings.Count > 0) return icmpPings.Min();
            }

            IPAddress targetIp = resolvedIp ?? await ResolveHostAsync(host, timeoutSec);
            if (targetIp == null) return -1;

            var pings = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int ping = await PingHostAsync(host, port, timeoutSec, targetIp);
                if (ping > 0) pings.Add(ping);
                if (i + 1 < count) await Task.Delay(20);
            }
            return pings.Count > 0 ? pings.Min() : -1;
        }

        public static async Task<int> PingViaLocalProxyAsync(string url, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                url = "https://www.gstatic.com/generate_204";
            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = Config.CreateLocalHttpProxy(),
                    UseProxy = true
                };
                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(Math.Clamp(PingTimeoutSec, 2, 15))
                };
                var sw = Stopwatch.StartNew();
                var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                sw.Stop();
                if (resp.IsSuccessStatusCode || (int)resp.StatusCode == 204)
                    return Math.Max(1, (int)sw.ElapsedMilliseconds);
            }
            catch { }
            return -1;
        }

        public static async Task<int> PingServerAsync(ServerInfo server)
        {
            if (server == null) return -1;
            if (IsProxyPingMode(PingMethod))
            {
                await PingServersProxyAsync(new List<ServerInfo> { server }, PingMethod, PingTestUrl, PingTimeoutSec);
                return server.Ping;
            }
            int ping = await PingHostMultiAsync(server.Host, server.Port);
            server.Ping = ping;
            return ping;
        }

        public static void PingServersParallel(List<ServerInfo> servers)
        {
            PingServersParallelAsync(servers).GetAwaiter().GetResult();
        }

        public static async Task PingServersParallelAsync(List<ServerInfo> servers)
        {
            if (servers == null || servers.Count == 0) return;

            if (IsProxyPingMode(PingMethod))
            {
                await PingServersProxyAsync(servers, PingMethod, PingTestUrl, PingTimeoutSec);
                return;
            }

            int timeoutSec = Math.Clamp(PingTimeoutSec, 1, 15);

            // Pre-resolve unique hosts before timing TCP handshakes to prevent DNS bursts and ThreadPool starvation
            var dnsCache = new ConcurrentDictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
            var uniqueHosts = servers.Select(s => s.Host).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            using (var dnsSem = new SemaphoreSlim(ParallelPingLimit))
            {
                await Task.WhenAll(uniqueHosts.Select(async h =>
                {
                    await dnsSem.WaitAsync();
                    try
                    {
                        var ip = await ResolveHostAsync(h, timeoutSec);
                        if (ip != null) dnsCache[h] = ip;
                    }
                    finally { dnsSem.Release(); }
                }));
            }

            using var pingSem = new SemaphoreSlim(ParallelPingLimit);
            await Task.WhenAll(servers.Select(async server =>
            {
                if (string.IsNullOrWhiteSpace(server.Host)) return;
                await pingSem.WaitAsync();
                try
                {
                    dnsCache.TryGetValue(server.Host, out var ip);
                    server.Ping = await PingHostMultiAsync(server.Host, server.Port, Config.PingMultiCount, ip);
                }
                finally { pingSem.Release(); }
            }));
        }

        private static bool IsProxyPingMode(string method)
        {
            return method is "proxy" or "http_get" or "http_head";
        }

        private static List<int> AllocateFreePorts(int count)
        {
            var listeners = new List<TcpListener>(count);
            var ports = new List<int>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var l = new TcpListener(IPAddress.Loopback, 0);
                    l.Start();
                    listeners.Add(l);
                    ports.Add(((IPEndPoint)l.LocalEndpoint).Port);
                }
            }
            finally
            {
                foreach (var l in listeners)
                {
                    try { l.Stop(); } catch { }
                }
            }
            return ports;
        }

        private static async Task PingServersProxyAsync(List<ServerInfo> servers, string mode, string testUrl, int timeoutSec)
        {
            int validTimeout = Math.Clamp(timeoutSec, 1, 15);
            string url = string.IsNullOrWhiteSpace(testUrl) ? "https://www.gstatic.com/generate_204" : testUrl.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            var singboxList = new List<ServerInfo>();
            var xrayList = new List<ServerInfo>();

            foreach (var s in servers)
            {
                if (string.IsNullOrWhiteSpace(s.Uri)) { s.Ping = -1; continue; }
                bool useSingbox = s.Protocol is "Hysteria" or "Hysteria2" or "TUIC" or "AnyTLS";
                if (useSingbox) singboxList.Add(s);
                else xrayList.Add(s);
            }

            const int batchSize = 20;
            for (int i = 0; i < singboxList.Count; i += batchSize)
            {
                var batch = singboxList.Skip(i).Take(batchSize).ToList();
                await RunCorePingBatchAsync(batch, useSingbox: true, mode, url, validTimeout);
            }
            for (int i = 0; i < xrayList.Count; i += batchSize)
            {
                var batch = xrayList.Skip(i).Take(batchSize).ToList();
                await RunCorePingBatchAsync(batch, useSingbox: false, mode, url, validTimeout);
            }
        }

        private static async Task RunCorePingBatchAsync(List<ServerInfo> batch, bool useSingbox, string mode, string testUrl, int timeoutSec)
        {
            string coreName = useSingbox ? "sing-box.exe" : "xray.exe";
            string corePath = ProcessManager.ResolveOrExtractCore(coreName, AppDir);
            if (!File.Exists(corePath))
            {
                foreach (var s in batch) s.Ping = -1;
                return;
            }

            var items = new List<(ServerInfo Server, JObject Outbound, int Port)>();
            var ports = AllocateFreePorts(batch.Count);

            for (int i = 0; i < batch.Count; i++)
            {
                var srv = batch[i];
                JObject ob = useSingbox
                    ? ConfigBuilder.BuildSingboxOutboundFromUri(srv.Uri, "prefer_ipv4", false)
                    : ConfigBuilder.BuildXrayOutboundFromUri(srv.Uri);

                if (ob == null)
                {
                    srv.Ping = -1;
                    continue;
                }
                ob["tag"] = $"out-{i}";
                items.Add((srv, ob, ports[i]));
            }

            if (items.Count == 0) return;

            JObject cfg;
            if (useSingbox)
            {
                var inbounds = new JArray();
                var outbounds = new JArray();
                var rules = new JArray();
                for (int i = 0; i < items.Count; i++)
                {
                    inbounds.Add(new JObject
                    {
                        ["type"] = "http",
                        ["tag"] = $"in-{i}",
                        ["listen"] = "127.0.0.1",
                        ["listen_port"] = items[i].Port
                    });
                    items[i].Outbound["domain_resolver"] = new JObject { ["server"] = "google", ["strategy"] = "prefer_ipv4" };
                    outbounds.Add(items[i].Outbound);
                    rules.Add(new JObject
                    {
                        ["inbound"] = new JArray($"in-{i}"),
                        ["action"] = "route",
                        ["outbound"] = items[i].Outbound["tag"].ToString()
                    });
                }
                outbounds.Add(new JObject { ["type"] = "direct", ["tag"] = "direct" });
                cfg = new JObject
                {
                    ["log"] = new JObject { ["level"] = "fatal" },
                    ["dns"] = new JObject
                    {
                        ["servers"] = new JArray
                        {
                            new JObject { ["type"] = "udp", ["tag"] = "google", ["server"] = "8.8.8.8" },
                            new JObject { ["type"] = "local", ["tag"] = "local" }
                        },
                        ["strategy"] = "ipv4_only"
                    },
                    ["inbounds"] = inbounds,
                    ["outbounds"] = outbounds,
                    ["route"] = new JObject
                    {
                        ["rules"] = rules,
                        ["default_domain_resolver"] = new JObject { ["server"] = "google", ["strategy"] = "prefer_ipv4" }
                    }
                };
            }
            else
            {
                var inbounds = new JArray();
                var outbounds = new JArray();
                var rules = new JArray();
                for (int i = 0; i < items.Count; i++)
                {
                    inbounds.Add(new JObject
                    {
                        ["tag"] = $"in-{i}",
                        ["listen"] = "127.0.0.1",
                        ["port"] = items[i].Port,
                        ["protocol"] = "http"
                    });
                    outbounds.Add(items[i].Outbound);
                    rules.Add(new JObject
                    {
                        ["type"] = "field",
                        ["inboundTag"] = new JArray($"in-{i}"),
                        ["outboundTag"] = items[i].Outbound["tag"].ToString()
                    });
                }
                cfg = new JObject
                {
                    ["log"] = new JObject { ["loglevel"] = "none" },
                    ["dns"] = new JObject { ["servers"] = new JArray("8.8.8.8", "1.1.1.1"), ["queryStrategy"] = "UseIPv4" },
                    ["inbounds"] = inbounds,
                    ["outbounds"] = outbounds,
                    ["routing"] = new JObject { ["rules"] = rules }
                };
            }

            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hivox", "cache");
            Directory.CreateDirectory(cacheDir);
            string tmpFile = Path.Combine(cacheDir, $"hivox_ping_{Guid.NewGuid():N}.json");
            Process proc = null;
            try
            {
                await File.WriteAllTextAsync(tmpFile, cfg.ToString(Formatting.None));
                var psi = new ProcessStartInfo
                {
                    FileName = corePath,
                    Arguments = $"run -c \"{tmpFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                proc = Process.Start(psi);
                if (proc == null)
                {
                    foreach (var it in items) it.Server.Ping = -1;
                    return;
                }

                // Wait until the first local listener port starts accepting connections
                int firstPort = items[0].Port;
                bool ready = false;
                for (int attempt = 0; attempt < 40 && !proc.HasExited; attempt++)
                {
                    try
                    {
                        using var probe = new TcpClient();
                        await probe.ConnectAsync(IPAddress.Loopback, firstPort);
                        ready = true;
                        break;
                    }
                    catch
                    {
                        await Task.Delay(50);
                    }
                }

                if (!ready || proc.HasExited)
                {
                    foreach (var it in items) it.Server.Ping = -1;
                    return;
                }

                using var sem = new SemaphoreSlim(6);
                await Task.WhenAll(items.Select(async item =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        item.Server.Ping = await ProbeViaHttpProxyAsync(item.Port, mode, testUrl, timeoutSec);
                    }
                    finally { sem.Release(); }
                }));
            }
            catch (Exception ex)
            {
                Logger.Log($"Proxy ping batch failed: {ex.Message}");
                foreach (var it in items) it.Server.Ping = -1;
            }
            finally
            {
                if (proc != null)
                {
                    try { if (!proc.HasExited) proc.Kill(true); } catch { }
                    try { proc.Dispose(); } catch { }
                }
                try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
            }
        }

        private static async Task<int> ProbeViaHttpProxyAsync(int port, string mode, string testUrl, int timeoutSec)
        {
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                    UseProxy = true,
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectTimeout = TimeSpan.FromSeconds(timeoutSec)
                };
                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(timeoutSec)
                };

                if (mode == "proxy")
                {
                    // Warm up the TLS tunnel with request #1, then measure pure 1-RTT HTTP roundtrip
                    using var warmReq = new HttpRequestMessage(HttpMethod.Head, testUrl);
                    using var warmResp = await client.SendAsync(warmReq, HttpCompletionOption.ResponseHeadersRead);
                    if ((int)warmResp.StatusCode >= 500) return -1;

                    var sw = Stopwatch.StartNew();
                    using var pingReq = new HttpRequestMessage(HttpMethod.Head, testUrl);
                    using var pingResp = await client.SendAsync(pingReq, HttpCompletionOption.ResponseHeadersRead);
                    sw.Stop();
                    if ((int)pingResp.StatusCode >= 500) return -1;
                    return Math.Max(1, (int)sw.ElapsedMilliseconds);
                }
                else
                {
                    var httpMethod = mode == "http_get" ? HttpMethod.Get : HttpMethod.Head;
                    var sw = Stopwatch.StartNew();
                    using var req = new HttpRequestMessage(httpMethod, testUrl);
                    using var resp = await client.SendAsync(req, mode == "http_get" ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead);
                    sw.Stop();
                    if ((int)resp.StatusCode >= 500) return -1;
                    return Math.Max(1, (int)sw.ElapsedMilliseconds);
                }
            }
            catch { return -1; }
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double b = Math.Max(0, bytes);
            int unitIndex = 0;
            while (b >= 1024 && unitIndex < units.Length - 1)
            {
                b /= 1024;
                unitIndex++;
            }
            return $"{b:F1} {units[unitIndex]}";
        }

        public static string FormatSpeed(double bytesPerSec)
        {
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
            double b = Math.Max(0, bytesPerSec);
            int unitIndex = 0;
            while (b >= 1024 && unitIndex < units.Length - 1)
            {
                b /= 1024;
                unitIndex++;
            }
            return unitIndex == 0 ? $"{b:F0} {units[unitIndex]}" : $"{b:F1} {units[unitIndex]}";
        }

        public static string FormatDuration(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}
