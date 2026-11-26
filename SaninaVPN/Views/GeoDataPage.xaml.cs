using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Hivox.Services;

namespace Hivox.Views
{
    public partial class GeoDataPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private CancellationTokenSource _cts;

        public GeoDataPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += OnLoaded;
            Unloaded += (_, _) => _cts?.Cancel();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            GeoAutoRow.IsChecked = vm.GetBool("geo_auto_update", true);
            int days = vm.GetInt("geo_update_interval_days", 7);
            if (days <= 3) Int3Radio.IsChecked = true;
            else if (days <= 7) Int7Radio.IsChecked = true;
            else if (days <= 14) Int14Radio.IsChecked = true;
            else Int30Radio.IsChecked = true;

            RefreshLocalStatus(vm);
        }

        private void RefreshLocalStatus(ViewModels.MainViewModel vm)
        {
            var (geoIp, geoSite) = GeoDataUpdater.GetLocalStatus(vm.AppDir);
            GeoIpSizeText.Text = geoIp.Exists ? NetworkUtils.FormatBytes(geoIp.SizeBytes) : "Не загружена";
            GeoIpShaText.Text = $"SHA-256: {geoIp.Sha256Prefix}";

            GeoSiteSizeText.Text = geoSite.Exists ? NetworkUtils.FormatBytes(geoSite.SizeBytes) : "Не загружена";
            GeoSiteShaText.Text = $"SHA-256: {geoSite.Sha256Prefix}";

            long lastUnix = vm.GetLong("geo_last_updated_unix", 0);
            if (lastUnix > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(lastUnix).LocalDateTime;
                LastUpdatedText.Text = $"Последнее обновление: {dt:dd.MM.yyyy HH:mm}";
            }
            else
            {
                LastUpdatedText.Text = "Последнее обновление: ещё не обновлялось";
            }
        }

        private async void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            UpdateNowBtn.IsEnabled = false;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Подготовка к загрузке...";

            var progress = new Progress<string>(msg =>
            {
                Dispatcher.Invoke(() => StatusText.Text = msg);
            });

            try
            {
                await GeoDataUpdater.UpdateAllAsync(vm.AppDir, vm.IsConnected, progress, _cts.Token);
                vm.Settings["geo_last_updated_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                vm.SaveData();
                RefreshLocalStatus(vm);
                StatusText.Text = "Geo-базы успешно обновлены и проверены по SHA-256!";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Загрузка отменена.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
            finally
            {
                UpdateNowBtn.IsEnabled = true;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            vm.Settings["geo_auto_update"] = GeoAutoRow.IsChecked;
            vm.Settings["geo_update_interval_days"] = Int3Radio.IsChecked == true ? 3
                : Int14Radio.IsChecked == true ? 14
                : Int30Radio.IsChecked == true ? 30
                : 7;

            vm.SaveData();
            _mainWindow.SettingsBack();
        }
    }
}
