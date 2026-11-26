using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.PlatformAndroid;
using Hivox.Mobile.Services;
using Hivox.Mobile.Services.PlatformAndroid;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class ConnectionCheckPage : ContentPage
{
    private readonly MainViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _isNavigating;
    private string _lastReport = "";

    public ConnectionCheckPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isNavigating = false;
        await RunDiagnosticsAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cts?.Cancel();
    }

    private Color Resolve(string key, string fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Color.FromArgb(fallback);

    private async Task RunDiagnosticsAsync()
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;

        RetryBtn.IsEnabled = false;
        CheckSpinner.IsRunning = true;
        CheckSpinner.IsVisible = true;
        SummaryTitle.Text = "Выполняется проверка...";
        SummarySubtitle.Text = _vm.IsConnected ? "Проверяем туннель: IP, DNS и задержку" : "Проверяем сеть напрямую";
        SummaryBadge.BackgroundColor = Resolve("M3PrimaryContainer", "#2A1B3D");
        SummaryBadge.Stroke = new SolidColorBrush(Resolve("M3Primary", "#7C4DFF"));
        SummaryIcon.Text = "\uE1D8";
        SummaryIcon.TextColor = Resolve("M3Primary", "#7C4DFF");

        IpValueLabel.Text = "...";
        LocationValueLabel.Text = "...";
        ConfiguredDnsLabel.Text = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns;
        DnsCheckLabel.Text = "...";
        TcpPingLabel.Text = "...";
        RealPingLabel.Text = _vm.IsConnected ? "..." : "VPN выкл.";

        string xrayState = XrayProcessManager.IsRunning ? "Работает" : "Остановлен";
        string tunState = _vm.IsTunMode
            ? (SaninaVpnService.IsRunning ? "Активен" : "Неактивен")
            : "Proxy";
        CoreStateLabel.Text = $"{_vm.ModeShortLabel} · {xrayState} · {tunState}";
        ServerStateLabel.Text = _vm.SelectedServer != null
            ? $"{_vm.SelectedServer.Name} ({_vm.SelectedServer.Protocol})"
            : "Не выбран";

        HttpClient CreateClient(int timeoutSec = 7)
        {
            if (_vm.IsConnected)
            {
                var handler = new HttpClientHandler
                {
                    Proxy = Config.CreateLocalHttpProxy(),
                    UseProxy = true
                };
                return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSec) };
            }
            return new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
        }

        string detectedIp = "";
        string locationStr = "Не определено";
        bool ipOk = false;

        var ipTask = Task.Run(async () =>
        {
            try
            {
                using var client = CreateClient(7);
                string ipResp = await client.GetStringAsync("https://api.ipify.org?format=json", ct);
                detectedIp = JObject.Parse(ipResp)["ip"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(detectedIp))
                {
                    ipOk = true;
                    try
                    {
                        string geoResp = await client.GetStringAsync($"https://ipwho.is/{detectedIp}", ct);
                        var g = JObject.Parse(geoResp);
                        if (g["success"]?.ToObject<bool>() == true)
                        {
                            string country = g["country"]?.ToString() ?? "";
                            string city = g["city"]?.ToString() ?? "";
                            string isp = g["connection"]?["isp"]?.ToString() ?? "";
                            string loc = string.IsNullOrEmpty(city) ? country : $"{country}, {city}";
                            locationStr = string.IsNullOrEmpty(isp) ? loc : $"{loc} · {isp}";
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }, ct);

        bool dnsOk = false;
        int dnsMs = -1;
        var dnsTask = Task.Run(async () =>
        {
            try
            {
                using var client = CreateClient(7);
                var req = new HttpRequestMessage(HttpMethod.Get, "https://cloudflare-dns.com/dns-query?name=example.com&type=A");
                req.Headers.Add("Accept", "application/dns-json");
                var sw = Stopwatch.StartNew();
                var resp = await client.SendAsync(req, ct);
                sw.Stop();
                if (resp.IsSuccessStatusCode)
                {
                    dnsOk = true;
                    dnsMs = Math.Max(1, (int)sw.ElapsedMilliseconds);
                }
            }
            catch { }
        }, ct);

        int tcpPing = -1;
        var tcpTask = Task.Run(async () =>
        {
            if (_vm.SelectedServer != null)
                tcpPing = await NetworkUtils.PingHostMultiAsync(_vm.SelectedServer.Host, _vm.SelectedServer.Port, 2, ct);
        }, ct);

        int realPing = -1;
        var realPingTask = Task.Run(async () =>
        {
            if (_vm.IsConnected)
                realPing = await NetworkUtils.PingViaProxyAsync(Config.PingTestUrl, HttpMethod.Get, ct);
        }, ct);

        try
        {
            await Task.WhenAll(ipTask, dnsTask, tcpTask, realPingTask);
        }
        catch { }

        if (ct.IsCancellationRequested) return;

        IpValueLabel.Text = ipOk ? detectedIp : "Ошибка определения";
        IpValueLabel.TextColor = ipOk ? Resolve("M3OnSurface", "#E6E1F5") : Resolve("M3Red", "#F87171");
        LocationValueLabel.Text = ipOk ? locationStr : "Нет ответа";

        if (ipOk && _vm.IsConnected)
            _vm.IpText = detectedIp;

        DnsCheckLabel.Text = dnsOk ? $"OK ({dnsMs} мс)" : "Ошибка DNS";
        DnsCheckLabel.TextColor = dnsOk ? Resolve("M3Green", "#4ADE80") : Resolve("M3Red", "#F87171");

        TcpPingLabel.Text = tcpPing > 0 ? $"{tcpPing} мс" : (_vm.SelectedServer == null ? "Узел не выбран" : "Недоступен");
        TcpPingLabel.TextColor = tcpPing > 0 ? Resolve("M3Green", "#4ADE80") : Resolve("M3OnSurfaceVariant", "#B9B0D8");

        if (_vm.IsConnected)
        {
            RealPingLabel.Text = realPing > 0 ? $"{realPing} мс" : "Тайм-аут";
            RealPingLabel.TextColor = realPing > 0 ? Resolve("M3Green", "#4ADE80") : Resolve("M3Red", "#F87171");
        }
        else
        {
            RealPingLabel.Text = "VPN выключен";
            RealPingLabel.TextColor = Resolve("M3OnSurfaceVariant", "#B9B0D8");
        }

        CheckSpinner.IsRunning = false;
        CheckSpinner.IsVisible = false;
        RetryBtn.IsEnabled = true;

        bool healthy = ipOk && dnsOk && (!_vm.IsConnected || realPing > 0);
        if (healthy && _vm.IsConnected)
        {
            var green = Resolve("M3Green", "#4ADE80");
            SummaryBadge.BackgroundColor = Resolve("M3GreenContainer", "#123426");
            SummaryBadge.Stroke = new SolidColorBrush(green.WithAlpha(0.45f));
            SummaryIcon.Text = "\uE86C";
            SummaryIcon.TextColor = green;
            SummaryTitle.Text = "Соединение стабильно";
            SummarySubtitle.Text = $"Трафик идёт через {_vm.ModeShortLabel} ({detectedIp})";
        }
        else if (healthy)
        {
            var amber = Resolve("M3Amber", "#FBBF24");
            SummaryBadge.BackgroundColor = Color.FromArgb("#24F59E0B");
            SummaryBadge.Stroke = new SolidColorBrush(Color.FromArgb("#44F59E0B"));
            SummaryIcon.Text = "\uE88E";
            SummaryIcon.TextColor = amber;
            SummaryTitle.Text = "Сеть доступна (без VPN)";
            SummarySubtitle.Text = $"Прямой IP: {detectedIp}";
        }
        else
        {
            var red = Resolve("M3Red", "#F87171");
            SummaryBadge.BackgroundColor = Resolve("M3RedContainer", "#3D1724");
            SummaryBadge.Stroke = new SolidColorBrush(red.WithAlpha(0.45f));
            SummaryIcon.Text = "\uE000";
            SummaryIcon.TextColor = red;
            SummaryTitle.Text = "Обнаружена проблема";
            SummarySubtitle.Text = _vm.IsConnected
                ? "Нет отклика через туннель — проверьте узел или DNS"
                : "Нет доступа к интернету";
        }

        _lastReport =
            $"=== Hivox Диагностика ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===\n" +
            $"Режим: {CoreStateLabel.Text}\n" +
            $"Узел: {ServerStateLabel.Text}\n" +
            $"Внешний IP: {IpValueLabel.Text}\n" +
            $"Локация: {LocationValueLabel.Text}\n" +
            $"DNS ({ConfiguredDnsLabel.Text}): {DnsCheckLabel.Text}\n" +
            $"TCP Ping: {TcpPingLabel.Text}\n" +
            $"Real Ping (HTTP 204): {RealPingLabel.Text}\n" +
            $"GeoIP / GeoSite: {(_vm.GeoDataSummary)}\n";
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e) => await RunDiagnosticsAsync();
    private async void OnRetryClicked(object? sender, EventArgs e) => await RunDiagnosticsAsync();

    private async void OnCopyReportClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastReport)) return;
        await Clipboard.Default.SetTextAsync(_lastReport);
        await HivoxDialog.ShowAlertAsync("Отчёт скопирован", "Отчёт о проверке соединения скопирован в буфер обмена.", "OK");
    }

    private async void OnOpenLogsClicked(object? sender, EventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("diagnostics");
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }
}
