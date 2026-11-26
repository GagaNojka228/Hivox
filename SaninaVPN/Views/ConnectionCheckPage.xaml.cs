using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using Hivox.Services;

namespace Hivox.Views
{
    public partial class ConnectionCheckPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private CancellationTokenSource _cts;
        private string _lastReport = "";

        public ConnectionCheckPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += async (_, _) => await RunCheckAsync();
            Unloaded += (_, _) => _cts?.Cancel();
        }

        private Brush ResolveBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

        private async Task RunCheckAsync()
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var ct = cts.Token;

            RetryBtn.IsEnabled = false;
            HeaderRefreshBtn.IsEnabled = false;

            SummaryTitle.Text = "Выполняется проверка...";
            SummarySubtitle.Text = vm.IsConnected ? "Проверяем туннель: IP, DNS и задержку" : "Проверяем сеть напрямую";
            SummaryBadge.Background = ResolveBrush("M3PrimaryContainerBrush");
            SummaryIcon.Fill = ResolveBrush("M3PrimaryBrush");

            IpValueText.Text = "...";
            IpValueText.Foreground = ResolveBrush("M3OnSurfaceBrush");
            LocationValueText.Text = "...";
            ConfiguredDnsText.Text = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns;
            DnsCheckText.Text = "...";
            DnsCheckText.Foreground = ResolveBrush("M3OnSurfaceBrush");
            TcpPingText.Text = "...";
            RealPingText.Text = vm.IsConnected ? "..." : "VPN выкл.";

            string modeStr = vm.ConnectionMode == Models.ConnectionMode.Tun ? "TUN" : "Proxy";
            string coreStr = vm.CoreType == Models.CoreType.SingBox ? "Sing-box" : "Xray";
            CoreStateText.Text = $"{modeStr} · {coreStr} · {(vm.IsConnected ? "Подключено" : "Отключено")}";
            ServerStateText.Text = vm.SelectedServer != null
                ? $"{vm.SelectedServer.Name} ({vm.SelectedServer.Protocol})"
                : "Не выбран";

            HttpClient CreateClient(int timeoutSec = 7)
            {
                if (vm.IsConnected)
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
                if (vm.SelectedServer != null)
                    tcpPing = await NetworkUtils.PingHostMultiAsync(vm.SelectedServer.Host, vm.SelectedServer.Port, 2);
            }, ct);

            int realPing = -1;
            var realPingTask = Task.Run(async () =>
            {
                if (vm.IsConnected)
                    realPing = await NetworkUtils.PingViaLocalProxyAsync(NetworkUtils.PingTestUrl, ct);
            }, ct);

            try { await Task.WhenAll(ipTask, dnsTask, tcpTask, realPingTask); } catch { }
            if (ct.IsCancellationRequested) return;

            IpValueText.Text = ipOk ? detectedIp : "Ошибка определения";
            IpValueText.Foreground = ipOk ? ResolveBrush("M3OnSurfaceBrush") : ResolveBrush("M3RedBrush");
            LocationValueText.Text = ipOk ? locationStr : "Нет ответа";
            if (ipOk && vm.IsConnected)
                vm.IpText = $"IP: {detectedIp}";

            DnsCheckText.Text = dnsOk ? $"OK ({dnsMs} мс)" : "Ошибка DNS";
            DnsCheckText.Foreground = dnsOk ? ResolveBrush("M3GreenBrush") : ResolveBrush("M3RedBrush");

            TcpPingText.Text = tcpPing > 0 ? $"{tcpPing} мс" : (vm.SelectedServer == null ? "Узел не выбран" : "Недоступен");
            TcpPingText.Foreground = tcpPing > 0 ? ResolveBrush("M3GreenBrush") : ResolveBrush("M3OnSurfaceVariantBrush");

            if (vm.IsConnected)
            {
                RealPingText.Text = realPing > 0 ? $"{realPing} мс" : "Тайм-аут";
                RealPingText.Foreground = realPing > 0 ? ResolveBrush("M3GreenBrush") : ResolveBrush("M3RedBrush");
            }
            else
            {
                RealPingText.Text = "VPN выключен";
                RealPingText.Foreground = ResolveBrush("M3OnSurfaceVariantBrush");
            }

            RetryBtn.IsEnabled = true;
            HeaderRefreshBtn.IsEnabled = true;

            bool healthy = ipOk && dnsOk && (!vm.IsConnected || realPing > 0);
            if (healthy && vm.IsConnected)
            {
                SummaryBadge.Background = ResolveBrush("M3GreenContainerBrush");
                SummaryIcon.Fill = ResolveBrush("M3GreenBrush");
                SummaryTitle.Text = "Соединение стабильно";
                SummarySubtitle.Text = $"Трафик идёт через {modeStr} ({detectedIp})";
            }
            else if (healthy)
            {
                SummaryBadge.Background = ResolveBrush("M3PrimaryContainerBrush");
                SummaryIcon.Fill = ResolveBrush("M3PrimaryBrush");
                SummaryTitle.Text = "Сеть доступна (без VPN)";
                SummarySubtitle.Text = $"Прямой IP: {detectedIp}";
            }
            else
            {
                SummaryBadge.Background = ResolveBrush("M3RedContainerBrush");
                SummaryIcon.Fill = ResolveBrush("M3RedBrush");
                SummaryTitle.Text = "Обнаружена проблема";
                SummarySubtitle.Text = vm.IsConnected
                    ? "Нет отклика через туннель — проверьте узел или DNS"
                    : "Нет доступа к интернету";
            }

            _lastReport =
                $"=== Hivox Диагностика ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===\n" +
                $"Режим: {CoreStateText.Text}\n" +
                $"Узел: {ServerStateText.Text}\n" +
                $"Внешний IP: {IpValueText.Text}\n" +
                $"Локация: {LocationValueText.Text}\n" +
                $"DNS ({ConfiguredDnsText.Text}): {DnsCheckText.Text}\n" +
                $"TCP Ping: {TcpPingText.Text}\n" +
                $"Real Ping (HTTP 204): {RealPingText.Text}\n";
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();

        private void CopyReport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastReport)) return;
            Clipboard.SetText(_lastReport);
            MessageBox.Show("Отчёт о проверке соединения скопирован в буфер обмена.", "Скопировано");
        }

        private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.NavigateTo(new DiagnosticsPage(_mainWindow));
        }
    }
}
