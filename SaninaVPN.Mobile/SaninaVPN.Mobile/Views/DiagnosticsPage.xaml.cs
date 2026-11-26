using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.PlatformAndroid;
using Hivox.Mobile.Services;
using Hivox.Mobile.Services.PlatformAndroid;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class DiagnosticsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private CancellationTokenSource? _diagCts;
    private bool _isNavigating;

    public DiagnosticsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isNavigating = false;
        _diagCts?.Cancel();
        _diagCts = new CancellationTokenSource();
        _vm.LogsText = Logger.GetRecentLogs();
        string xrayState = XrayProcessManager.IsRunning ? "Работает" : "Остановлен";
        string tunState = SaninaVpnService.IsRunning ? "Активен" : "Неактивен";
        DiagResultLabel.Text = $"Режим: {_vm.ModeShortLabel} · Xray: {xrayState} · TUN: {tunState}";
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _diagCts?.Cancel();
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }

    private async void OnDnsTestClicked(object? sender, EventArgs e)
    {
        if (!_vm.IsConnected)
        {
            await HivoxDialog.ShowAlertAsync("Hivox", "Подключитесь к VPN для теста DNS.", "OK");
            return;
        }

        var token = _diagCts?.Token ?? CancellationToken.None;
        DnsTestBtn.IsEnabled = false;
        SpeedTestBtn.IsEnabled = false;
        DiagResultLabel.Text = "Проверка DNS и IP через прокси...";

        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = Config.CreateLocalHttpProxy(),
                UseProxy = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync("https://dnsleaktest.com", token);

            string ipText = "";
            try
            {
                var ipJson = await client.GetStringAsync("https://api.ipify.org?format=json", token);
                ipText = JObject.Parse(ipJson)["ip"]?.ToString() ?? "";
            }
            catch { }

            DiagResultLabel.Text = response.IsSuccessStatusCode
                ? $"[OK] DNS не утекает{(string.IsNullOrEmpty(ipText) ? "" : $" · Внешний IP: {ipText}")}"
                : $"[!] Возможна проблема с DNS (HTTP {(int)response.StatusCode})";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DiagResultLabel.Text = $"[!] Ошибка: {ex.Message}";
        }
        finally
        {
            DnsTestBtn.IsEnabled = true;
            SpeedTestBtn.IsEnabled = true;
            _vm.LogsText = Logger.GetRecentLogs();
        }
    }

    private async void OnSpeedTestClicked(object? sender, EventArgs e)
    {
        if (!_vm.IsConnected)
        {
            await HivoxDialog.ShowAlertAsync("Hivox", "Подключитесь к VPN для теста скорости.", "OK");
            return;
        }

        var token = _diagCts?.Token ?? CancellationToken.None;
        DnsTestBtn.IsEnabled = false;
        SpeedTestBtn.IsEnabled = false;
        DiagResultLabel.Text = "Загрузка 10 МБ через туннель...";

        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = Config.CreateLocalHttpProxy(),
                UseProxy = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(
                "https://speed.cloudflare.com/__down?bytes=10000000",
                HttpCompletionOption.ResponseHeadersRead,
                token);

            if (response.IsSuccessStatusCode)
            {
                long totalBytes = 0;
                var buffer = new byte[32768];
                using var stream = await response.Content.ReadAsStreamAsync(token);
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                    totalBytes += read;
                sw.Stop();

                double sizeMb = totalBytes / (1024.0 * 1024.0);
                double mbps = (sizeMb * 8.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                DiagResultLabel.Text = $"Скорость: {mbps:F1} Mbps\nЗагружено: {sizeMb:F1} MB за {sw.Elapsed.TotalSeconds:F1} сек.";
            }
            else
            {
                sw.Stop();
                DiagResultLabel.Text = $"Ошибка HTTP {(int)response.StatusCode}";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DiagResultLabel.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            DnsTestBtn.IsEnabled = true;
            SpeedTestBtn.IsEnabled = true;
            _vm.LogsText = Logger.GetRecentLogs();
        }
    }

    private async void OnCopyLogsClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(_vm.LogsText ?? "");
        await HivoxDialog.ShowAlertAsync("Скопировано", "Логи скопированы в буфер обмена.", "OK");
    }

    private void OnClearLogsClicked(object? sender, EventArgs e)
    {
        Logger.ClearLogs();
        _vm.LogsText = Logger.GetRecentLogs();
    }
}
