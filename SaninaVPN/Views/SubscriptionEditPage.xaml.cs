using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Hivox.Views
{
    public partial class SubscriptionEditPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private readonly string _groupName;
        private readonly string _url;
        private string _supportUrl = "";
        private string _webUrl = "";

        public string GroupTitle => _groupName;

        public SubscriptionEditPage(MainWindow mainWindow, string groupName, string url)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _groupName = groupName;
            _url = url;

            Loaded += OnLoaded;
            CustomIntervalRow.CheckedChanged += (_, _) => UpdateIntervalVisibility();
            SupportLinkRow.Click += (_, _) => OpenUrl(_supportUrl);
            WebLinkRow.Click += (_, _) => OpenUrl(_webUrl);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            var (_, meta) = vm.GetGroupMetadata(_groupName);

            NameBox.Text = !string.IsNullOrWhiteSpace(meta?.CustomName) ? meta.CustomName : _groupName;
            NameBox.IsEnabled = !string.IsNullOrWhiteSpace(_url);
            NeverCollapseRow.IsChecked = meta?.NeverCollapse == true;

            _supportUrl = meta?.SupportUrl ?? "";
            _webUrl = meta?.WebPageUrl ?? "";
            bool hasSupport = !string.IsNullOrWhiteSpace(_supportUrl);
            bool hasWeb = !string.IsNullOrWhiteSpace(_webUrl);
            LinksSection.Visibility = (hasSupport || hasWeb) ? Visibility.Visible : Visibility.Collapsed;
            SupportLinkRow.Visibility = hasSupport ? Visibility.Visible : Visibility.Collapsed;
            WebLinkRow.Visibility = hasWeb ? Visibility.Visible : Visibility.Collapsed;
            LinksDivider.Visibility = (hasSupport && hasWeb) ? Visibility.Visible : Visibility.Collapsed;

            int customMin = meta?.CustomUpdateIntervalMinutes ?? 0;
            CustomIntervalRow.IsChecked = customMin > 0;
            UpdateIntervalVisibility();

            switch (customMin)
            {
                case 30: Int30mRadio.IsChecked = true; break;
                case 60: Int1hRadio.IsChecked = true; break;
                case 360: Int6hRadio.IsChecked = true; break;
                case 720: Int12hRadio.IsChecked = true; break;
                case 1440: Int24hRadio.IsChecked = true; break;
                default: Int2hRadio.IsChecked = true; break;
            }

            var pingDisplay = Converters.PingToTextConverter.Normalize(vm.GetString("ping_display", "time"));
            DispTimeRadio.IsChecked = pingDisplay == "time";
            DispBarsRadio.IsChecked = pingDisplay == "bars";
            DispBothRadio.IsChecked = pingDisplay == "both";
            DispDotsRadio.IsChecked = pingDisplay == "dots";

            var pingType = vm.GetString("ping_type", "proxy");
            ProtoProxyRadio.IsChecked = pingType == "proxy";
            ProtoTcpRadio.IsChecked = pingType == "tcp" || pingType == "icmp";
            ProtoGetRadio.IsChecked = pingType == "http_get";
            ProtoHeadRadio.IsChecked = pingType == "http_head";

            PingUrlBox.Text = vm.GetString("ping_url", "https://www.gstatic.com/generate_204");

            string sort = meta?.CustomSort ?? "";
            SortPingRadio.IsChecked = sort == "ping";
            SortAlphaRadio.IsChecked = sort == "alpha";
            SortDefaultRadio.IsChecked = sort != "ping" && sort != "alpha";

            CustomDnsBox.Text = meta?.CustomDns ?? "";
            DnsPlaceholder.Visibility = string.IsNullOrEmpty(CustomDnsBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateIntervalVisibility()
        {
            IntervalChipsPanel.Visibility = CustomIntervalRow.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CustomDnsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            DnsPlaceholder.Visibility = string.IsNullOrEmpty(CustomDnsBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            int customMin = 0;
            if (CustomIntervalRow.IsChecked)
            {
                customMin = Int30mRadio.IsChecked == true ? 30
                    : Int1hRadio.IsChecked == true ? 60
                    : Int6hRadio.IsChecked == true ? 360
                    : Int12hRadio.IsChecked == true ? 720
                    : Int24hRadio.IsChecked == true ? 1440
                    : 120;
            }

            string pType = ProtoTcpRadio.IsChecked == true ? "tcp"
                : ProtoGetRadio.IsChecked == true ? "http_get"
                : ProtoHeadRadio.IsChecked == true ? "http_head"
                : "proxy";

            string pDisplay = DispBarsRadio.IsChecked == true ? "bars"
                : DispBothRadio.IsChecked == true ? "both"
                : DispDotsRadio.IsChecked == true ? "dots"
                : "time";

            string sort = SortPingRadio.IsChecked == true ? "ping"
                : SortAlphaRadio.IsChecked == true ? "alpha"
                : "default";

            vm.Settings["ping_type"] = pType;
            vm.Settings["ping_display"] = pDisplay;
            vm.Settings["ping_url"] = string.IsNullOrWhiteSpace(PingUrlBox.Text)
                ? "https://www.gstatic.com/generate_204"
                : PingUrlBox.Text.Trim();
            Services.NetworkUtils.PingMethod = pType;
            Services.NetworkUtils.PingTestUrl = vm.GetString("ping_url", "https://www.gstatic.com/generate_204");

            vm.ApplySubscriptionEdits(
                _groupName,
                _url,
                NameBox.Text,
                NeverCollapseRow.IsChecked,
                customMin,
                CustomDnsBox.Text,
                sort);

            _mainWindow.RefreshServerListView();
            _mainWindow.RefreshGroupHeaders();
            _mainWindow.SettingsBack();
        }
    }
}
