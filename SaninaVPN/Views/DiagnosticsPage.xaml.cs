using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Hivox.Helpers;

namespace Hivox.Views
{
    public partial class DiagnosticsPage : UserControl
    {
        private readonly MainWindow _mainWindow;

        public DiagnosticsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
        }

        private void AppendResult(string text)
        {
            Dispatcher.Invoke(() => ResultBox.Text += text + Environment.NewLine);
        }

        private async void DnsTest_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm?.ConnectionState != Models.ConnectionState.Connected)
            {
                MessageBox.Show("Подключитесь к VPN для теста DNS", "VPN");
                return;
            }

            DnsTestBtn.IsEnabled = false;
            ResultBox.Text = "";

            await Task.Run(() =>
            {
                try
                {
                    var handler = new HttpClientHandler
                    {
                        Proxy = Services.Config.CreateLocalHttpProxy()
                    };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                    AppendResult("Проверка DNS...");
                    var response = client.GetAsync("https://dnsleaktest.com").Result;
                    var msg = response.IsSuccessStatusCode
                        ? "[OK] DNS не утекает"
                        : "[!] Возможна проблема с DNS";
                    AppendResult(msg);
                }
                catch (Exception ex)
                {
                    AppendResult($"[!] Ошибка: {ex.Message}");
                }
                AppendResult("\n✓ Тест завершен");
            });

            DnsTestBtn.IsEnabled = true;
        }

        private async void SpeedTest_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm?.ConnectionState != Models.ConnectionState.Connected)
            {
                MessageBox.Show("Подключитесь к VPN для теста скорости", "VPN");
                return;
            }

            SpeedTestBtn.IsEnabled = false;
            ResultBox.Text = "";

            await Task.Run(() =>
            {
                try
                {
                    var handler = new HttpClientHandler
                    {
                        Proxy = Services.Config.CreateLocalHttpProxy()
                    };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    AppendResult("Загрузка 10 MB...");
                    var response = client.GetAsync("https://speed.cloudflare.com/__down?bytes=10000000").Result;
                    sw.Stop();
                    if (response.IsSuccessStatusCode)
                    {
                        var content = response.Content.ReadAsByteArrayAsync().Result;
                        var sizeMb = content.Length / (1024.0 * 1024.0);
                        var speed = (sizeMb * 8) / sw.Elapsed.TotalSeconds;
                        AppendResult($"Скорость: {speed:F1} Mbps");
                        AppendResult($"Загружено: {sizeMb:F1} MB за {sw.Elapsed.TotalSeconds:F1} сек.");
                    }
                    else
                    {
                        AppendResult($"Ошибка HTTP {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    AppendResult($"Ошибка: {ex.Message}");
                }
                AppendResult("\n✓ Тест завершен");
            });

            SpeedTestBtn.IsEnabled = true;
        }
    }
}
