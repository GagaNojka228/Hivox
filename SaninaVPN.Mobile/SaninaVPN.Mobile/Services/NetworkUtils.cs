using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Hivox.Mobile.Models;

namespace Hivox.Mobile.Services;

public static class NetworkUtils
{
    public static async Task<int> PingHostAsync(string host, int port = 443, int? timeoutSec = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return -1;
        if (port <= 0 || port > 65535) port = 443;
        int toSec = Math.Clamp(timeoutSec ?? Config.PingTimeout, 1, 15);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(toSec));

            var sw = Stopwatch.StartNew();
            using var client = new TcpClient();
            await client.ConnectAsync(host.Trim('[', ']'), port, timeoutCts.Token).ConfigureAwait(false);
            sw.Stop();
            return Math.Max(1, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return -1;
        }
    }

    public static async Task<int> PingHostMultiAsync(string host, int port = 443, int count = Config.PingMultiCount, CancellationToken ct = default)
    {
        var pings = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (ct.IsCancellationRequested) break;
            int ping = await PingHostAsync(host, port, Config.PingTimeout, ct).ConfigureAwait(false);
            if (ping > 0) pings.Add(ping);
            if (i < count - 1)
            {
                try { await Task.Delay(120, ct).ConfigureAwait(false); } catch { break; }
            }
        }
        return pings.Count > 0 ? (int)pings.Average() : -1;
    }

    public static async Task<int> PingViaProxyAsync(string? testUrl = null, HttpMethod? method = null, CancellationToken ct = default)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(testUrl) ? Config.PingTestUrl : testUrl;
            using var handler = new HttpClientHandler
            {
                Proxy = Config.CreateLocalHttpProxy(),
                UseProxy = true
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(Math.Clamp(Config.PingTimeout, 1, 15))
            };
            var sw = Stopwatch.StartNew();
            var req = new HttpRequestMessage(method ?? HttpMethod.Get, url);
            var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            sw.Stop();
            return resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NoContent
                ? Math.Max(1, (int)sw.ElapsedMilliseconds)
                : -1;
        }
        catch
        {
            return -1;
        }
    }

    public static async Task<int> PingServerAsync(ServerInfo server, bool useProxyIfAvailable = false, CancellationToken ct = default)
    {
        if (useProxyIfAvailable && Config.PingType is "proxy" or "http_get" or "http_head")
        {
            var m = Config.PingType == "http_head" ? HttpMethod.Head : HttpMethod.Get;
            return await PingViaProxyAsync(Config.PingTestUrl, m, ct).ConfigureAwait(false);
        }
        return await Task.Run(() => PingHostMultiAsync(server.Host, server.Port, Config.PingMultiCount, ct), ct).ConfigureAwait(false);
    }

    public static async Task PingServersParallelAsync(IReadOnlyList<ServerInfo> servers, CancellationToken ct = default)
    {
        if (servers.Count == 0) return;
        await Task.Run(async () =>
        {
            using var sem = new SemaphoreSlim(Config.PingParallel);
            var tasks = servers.Select(async s =>
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Host) || ct.IsCancellationRequested) return;
                await sem.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (ct.IsCancellationRequested) return;
                    int ping = await PingHostMultiAsync(s.Host, s.Port, Config.PingMultiCount, ct).ConfigureAwait(false);
                    if (!ct.IsCancellationRequested)
                        MainThread.BeginInvokeOnMainThread(() => s.Ping = ping);
                }
                catch { }
                finally
                {
                    sem.Release();
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
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
        if (bytesPerSec < 0) bytesPerSec = 0;
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):F2} MB/s";
    }

    public static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
