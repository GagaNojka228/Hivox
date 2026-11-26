using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hivox.Views
{
    public partial class PingSettingsPage : UserControl
    {
        private const string GoogleUrl = "https://www.gstatic.com/generate_204";
        private const string CloudflareUrl = "https://cp.cloudflare.com/generate_204";
        private const string AppleUrl = "http://captive.apple.com/hotspot-detect.html";

        private readonly MainWindow _mainWindow;
        private int _timeoutSec = 3;

        public PingSettingsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            var pingType = vm.GetString("ping_type", "proxy");
            ProtoProxyRadio.IsChecked = pingType == "proxy";
            ProtoTcpRadio.IsChecked = pingType == "tcp" || pingType == "icmp";
            ProtoGetRadio.IsChecked = pingType == "http_get";
            ProtoHeadRadio.IsChecked = pingType == "http_head";
            if (ProtoProxyRadio.IsChecked != true && ProtoTcpRadio.IsChecked != true &&
                ProtoGetRadio.IsChecked != true && ProtoHeadRadio.IsChecked != true)
                ProtoProxyRadio.IsChecked = true;

            var pingDisplay = Converters.PingToTextConverter.Normalize(vm.GetString("ping_display", "time"));
            DispTimeRadio.IsChecked = pingDisplay == "time";
            DispBarsRadio.IsChecked = pingDisplay == "bars";
            DispBothRadio.IsChecked = pingDisplay == "both";
            DispDotsRadio.IsChecked = pingDisplay == "dots";

            TestUrlBox.Text = vm.GetString("ping_url", GoogleUrl);
            _timeoutSec = Math.Clamp(vm.GetInt("ping_timeout", 3), 1, 15);
            UpdateTimeoutLabel();
            HighlightPreset();
        }

        private void PresetGoogle_Click(object sender, MouseButtonEventArgs e) => SetPreset(GoogleUrl);
        private void PresetCloudflare_Click(object sender, MouseButtonEventArgs e) => SetPreset(CloudflareUrl);
        private void PresetApple_Click(object sender, MouseButtonEventArgs e) => SetPreset(AppleUrl);

        private void SetPreset(string url)
        {
            TestUrlBox.Text = url;
            HighlightPreset();
        }

        private void TestUrlBox_TextChanged(object sender, TextChangedEventArgs e) => HighlightPreset();

        private void HighlightPreset()
        {
            string url = (TestUrlBox.Text ?? "").Trim();
            var primaryContainer = TryFindResource("M3PrimaryContainerBrush") as Brush ?? Brushes.Transparent;
            var primary = TryFindResource("M3PrimaryBrush") as Brush ?? Brushes.White;
            var onSurface = TryFindResource("M3OnSurfaceBrush") as Brush ?? Brushes.White;

            void Apply(Border card, TextBlock title, System.Windows.Shapes.Path check, bool active)
            {
                card.Background = active ? primaryContainer : Brushes.Transparent;
                title.Foreground = active ? primary : onSurface;
                check.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }

            Apply(PresetGoogleCard, PresetGoogleTitle, PresetGoogleCheck, string.Equals(url, GoogleUrl, StringComparison.OrdinalIgnoreCase));
            Apply(PresetCloudflareCard, PresetCloudflareTitle, PresetCloudflareCheck, string.Equals(url, CloudflareUrl, StringComparison.OrdinalIgnoreCase));
            Apply(PresetAppleCard, PresetAppleTitle, PresetAppleCheck, string.Equals(url, AppleUrl, StringComparison.OrdinalIgnoreCase));
        }

        private void TimeoutMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_timeoutSec > 1)
            {
                _timeoutSec--;
                UpdateTimeoutLabel();
            }
        }

        private void TimeoutPlus_Click(object sender, RoutedEventArgs e)
        {
            if (_timeoutSec < 15)
            {
                _timeoutSec++;
                UpdateTimeoutLabel();
            }
        }

        private void UpdateTimeoutLabel() => TimeoutValueText.Text = $"{_timeoutSec}s";

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            string pType = ProtoTcpRadio.IsChecked == true ? "tcp"
                : ProtoGetRadio.IsChecked == true ? "http_get"
                : ProtoHeadRadio.IsChecked == true ? "http_head"
                : "proxy";

            string pDisplay = DispBarsRadio.IsChecked == true ? "bars"
                : DispBothRadio.IsChecked == true ? "both"
                : DispDotsRadio.IsChecked == true ? "dots"
                : "time";

            string url = string.IsNullOrWhiteSpace(TestUrlBox.Text) ? GoogleUrl : TestUrlBox.Text.Trim();

            vm.Settings["ping_type"] = pType;
            vm.Settings["ping_display"] = pDisplay;
            vm.Settings["ping_url"] = url;
            vm.Settings["ping_timeout"] = _timeoutSec;

            Services.NetworkUtils.PingMethod = pType;
            Services.NetworkUtils.PingTestUrl = url;
            Services.NetworkUtils.PingTimeoutSec = _timeoutSec;
            Converters.PingToTextConverter.DisplayMode = pDisplay;

            vm.SaveData();
            _mainWindow.RefreshServerListView();
            _mainWindow.SettingsBack();
        }
    }
}
