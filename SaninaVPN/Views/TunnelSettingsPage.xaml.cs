using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Hivox.Views
{
    public partial class TunnelSettingsPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private bool _isLoading;
        private string _proxyUser = "";
        private string _proxyPass = "";

        public TunnelSettingsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            _isLoading = true;
            try
            {
                string dns = vm.GetString("custom_dns", "1.1.1.1");
                if (string.IsNullOrWhiteSpace(dns)) dns = "1.1.1.1";
                CustomDnsBox.Text = dns;
                DnsCloudflareRadio.IsChecked = dns == "1.1.1.1";
                DnsGoogleRadio.IsChecked = dns == "8.8.8.8";
                DnsQuad9Radio.IsChecked = dns == "9.9.9.9";
                DnsAdGuardRadio.IsChecked = dns == "94.140.14.14";

                int mtu = vm.GetInt("tun_mtu", 1420);
                if (mtu == 1500) Mtu1500Radio.IsChecked = true;
                else if (mtu == 1400) Mtu1400Radio.IsChecked = true;
                else if (mtu == 1280) Mtu1280Radio.IsChecked = true;
                else Mtu1420Radio.IsChecked = true;

                FragmentRow.IsChecked = vm.GetBool("fragmentation", false);
                string fragPkt = vm.GetString("fragment_packets", "tlshello");
                if (fragPkt == "1-3") Frag13Radio.IsChecked = true;
                else if (fragPkt == "1-5") Frag15Radio.IsChecked = true;
                else FragTlsHelloRadio.IsChecked = true;
                FragLengthBox.Text = vm.GetString("fragment_length", "100-200");
                FragIntervalBox.Text = vm.GetString("fragment_interval", "10-20");

                NoisesRow.IsChecked = vm.GetBool("noises_enabled", false);
                string nType = vm.GetString("noise_type", "rand");
                if (nType == "str") NoiseStrRadio.IsChecked = true;
                else if (nType == "base64") NoiseBase64Radio.IsChecked = true;
                else NoiseRandRadio.IsChecked = true;
                NoisePacketBox.Text = vm.GetString("noise_packet", "50-100");
                NoiseDelayBox.Text = vm.GetString("noise_delay", "10-15");

                MuxRow.IsChecked = vm.GetBool("mux_enabled", true);
                MuxConcBox.Text = Math.Clamp(vm.GetInt("mux_concurrency", 8), 1, 64).ToString();
                MuxXudpConcBox.Text = Math.Clamp(vm.GetInt("mux_xudp_concurrency", 16), 1, 128).ToString();
                string quic = vm.GetString("mux_xudp_quic", "reject");
                if (quic == "allow") QuicAllowRadio.IsChecked = true;
                else if (quic == "skip") QuicSkipRadio.IsChecked = true;
                else QuicRejectRadio.IsChecked = true;

                SniffRouteOnlyRow.IsChecked = vm.GetBool("sniffing_route_only", true);
                BlockUdpRow.IsChecked = vm.GetBool("block_udp", false);

                SocksAuthRow.IsChecked = vm.GetBool("proxy_auth_enabled", false);
                HttpAuthHotspotRow.IsChecked = vm.GetBool("http_auth_hotspot", true);
                _proxyUser = vm.GetString("proxy_auth_user", "");
                _proxyPass = vm.GetString("proxy_auth_password", "");
                if (string.IsNullOrWhiteSpace(_proxyUser) || string.IsNullOrWhiteSpace(_proxyPass))
                    GenerateCreds();
                else
                    UpdateCredsDisplay();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void DnsPreset_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (DnsCloudflareRadio.IsChecked == true) CustomDnsBox.Text = "1.1.1.1";
            else if (DnsGoogleRadio.IsChecked == true) CustomDnsBox.Text = "8.8.8.8";
            else if (DnsQuad9Radio.IsChecked == true) CustomDnsBox.Text = "9.9.9.9";
            else if (DnsAdGuardRadio.IsChecked == true) CustomDnsBox.Text = "94.140.14.14";
        }

        private void GenerateCreds()
        {
            _proxyUser = $"hivox_{Guid.NewGuid():N}"[..14];
            _proxyPass = Guid.NewGuid().ToString("N")[..12];
            UpdateCredsDisplay();
        }

        private void UpdateCredsDisplay()
        {
            ProxyUserText.Text = $"Логин: {_proxyUser}";
            ProxyPassText.Text = $"Пароль: {new string('•', Math.Max(8, _proxyPass.Length))}";
        }

        private void CopyCreds_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText($"Логин: {_proxyUser}\nПароль: {_proxyPass}");
            MessageBox.Show("Логин и пароль локального прокси скопированы в буфер обмена.", "Скопировано");
        }

        private void RegenCreds_Click(object sender, RoutedEventArgs e)
        {
            GenerateCreds();
        }

        private static bool TryParseIntOrRange(string raw, int minAllowed, out string normalized)
        {
            normalized = (raw ?? "").Trim();
            if (!Regex.IsMatch(normalized, @"^\d+(-\d+)?$")) return false;
            var parts = normalized.Split('-', 2);
            if (!int.TryParse(parts[0], out int a) || a < minAllowed) return false;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out int b) || b < a)) return false;
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            string dns = string.IsNullOrWhiteSpace(CustomDnsBox.Text) ? "1.1.1.1" : CustomDnsBox.Text.Trim();
            int mtu = Mtu1500Radio.IsChecked == true ? 1500
                : Mtu1400Radio.IsChecked == true ? 1400
                : Mtu1280Radio.IsChecked == true ? 1280
                : 1420;

            string fragPkt = Frag13Radio.IsChecked == true ? "1-3"
                : Frag15Radio.IsChecked == true ? "1-5"
                : "tlshello";
            string fragLen = TryParseIntOrRange(FragLengthBox.Text, 1, out var fl) ? fl : "100-200";
            string fragInt = TryParseIntOrRange(FragIntervalBox.Text, 0, out var fi) ? fi : "10-20";

            string nType = NoiseStrRadio.IsChecked == true ? "str"
                : NoiseBase64Radio.IsChecked == true ? "base64"
                : "rand";
            string nPkt = string.IsNullOrWhiteSpace(NoisePacketBox.Text) ? "50-100" : NoisePacketBox.Text.Trim();
            if (nType == "rand" && !TryParseIntOrRange(nPkt, 1, out nPkt)) nPkt = "50-100";
            string nDelay = TryParseIntOrRange(NoiseDelayBox.Text, 0, out var nd) ? nd : "10-15";

            int muxConc = int.TryParse(MuxConcBox.Text?.Trim(), out int mc) && mc is >= 1 and <= 64 ? mc : 8;
            int xudpConc = int.TryParse(MuxXudpConcBox.Text?.Trim(), out int xc) && xc is >= 1 and <= 128 ? xc : 16;
            string quic = QuicAllowRadio.IsChecked == true ? "allow"
                : QuicSkipRadio.IsChecked == true ? "skip"
                : "reject";

            vm.Settings["custom_dns"] = dns;
            vm.Settings["tun_mtu"] = mtu;
            vm.Settings["fragmentation"] = FragmentRow.IsChecked;
            vm.Settings["fragment_packets"] = fragPkt;
            vm.Settings["fragment_length"] = fragLen;
            vm.Settings["fragment_interval"] = fragInt;

            vm.Settings["noises_enabled"] = NoisesRow.IsChecked;
            vm.Settings["noise_type"] = nType;
            vm.Settings["noise_packet"] = nPkt;
            vm.Settings["noise_delay"] = nDelay;

            vm.Settings["mux_enabled"] = MuxRow.IsChecked;
            vm.Settings["mux_concurrency"] = muxConc;
            vm.Settings["mux_xudp_concurrency"] = xudpConc;
            vm.Settings["mux_xudp_quic"] = quic;

            vm.Settings["sniffing_route_only"] = SniffRouteOnlyRow.IsChecked;
            vm.Settings["block_udp"] = BlockUdpRow.IsChecked;

            vm.Settings["proxy_auth_enabled"] = SocksAuthRow.IsChecked;
            vm.Settings["http_auth_hotspot"] = HttpAuthHotspotRow.IsChecked;
            vm.Settings["proxy_auth_user"] = _proxyUser;
            vm.Settings["proxy_auth_password"] = _proxyPass;

            vm.ApplyRuntimeSettings();
            vm.SaveData();

            if (vm.IsConnected)
                _ = vm.RestartActiveConnectionAsync();

            _mainWindow.SettingsBack();
        }
    }
}
