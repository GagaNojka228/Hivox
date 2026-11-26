using Hivox.Mobile.Models;
using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class TunnelSettingsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isLoading;
    private bool _hasSaved;
    private bool _isHandlingBack;
    private bool _isNavigating;
    private bool _useTunMode = true;
    private string _fragmentPackets = "tlshello";
    private string _noiseType = "rand";
    private string _muxXudpQuic = "reject";
    private string _preferredIp = "auto";
    private string _customDns = "1.1.1.1";
    private int _tunMtu = 1500;
    private int _memoryLimitMb = 100;
    private string _proxyUser = "";
    private string _proxyPassword = "";
    private string _initialProxyUser = "";
    private string _initialProxyPassword = "";

    public TunnelSettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hasSaved = false;
        _isHandlingBack = false;
        _isNavigating = false;
        LoadSettings();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SaveChanges();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            _useTunMode = _vm.IsTunMode;

            NotifSpeedSwitch.IsToggled = _vm.GetBool("notif_show_speed", false);
            WakeLockSwitch.IsToggled = _vm.GetBool("keep_wakelock", true);

            FragmentSwitch.IsToggled = _vm.GetBool("fragmentation", false);
            _fragmentPackets = _vm.GetString("fragment_packets", "tlshello");
            if (string.IsNullOrWhiteSpace(_fragmentPackets)) _fragmentPackets = "tlshello";
            FragmentPacketsLabel.Text = _fragmentPackets;

            string fragLen = _vm.GetString("fragment_length", "100-200");
            FragmentLengthEntry.Text = string.IsNullOrWhiteSpace(fragLen) ? "100-200" : fragLen;

            string fragInt = _vm.GetString("fragment_interval", "10-20");
            FragmentIntervalEntry.Text = string.IsNullOrWhiteSpace(fragInt) ? "10-20" : fragInt;

            NoisesSwitch.IsToggled = _vm.GetBool("noises_enabled", false);
            _noiseType = _vm.GetString("noise_type", "rand");
            if (string.IsNullOrWhiteSpace(_noiseType)) _noiseType = "rand";
            NoiseTypeLabel.Text = _noiseType;

            string noisePkt = _vm.GetString("noise_packet", "50-100");
            NoisePacketEntry.Text = string.IsNullOrWhiteSpace(noisePkt) ? "50-100" : noisePkt;

            string noiseDly = _vm.GetString("noise_delay", "10-15");
            NoiseDelayEntry.Text = string.IsNullOrWhiteSpace(noiseDly) ? "10-15" : noiseDly;

            MuxSwitch.IsToggled = _vm.GetBool("mux_enabled", true);
            int muxConc = Math.Clamp(_vm.GetInt("mux_concurrency", 8), 1, 64);
            MuxConcurrencyEntry.Text = muxConc.ToString();

            int xudpConc = Math.Clamp(_vm.GetInt("mux_xudp_concurrency", 16), 1, 128);
            MuxXudpConcurrencyEntry.Text = xudpConc.ToString();

            _muxXudpQuic = _vm.GetString("mux_xudp_quic", "reject");
            if (string.IsNullOrWhiteSpace(_muxXudpQuic)) _muxXudpQuic = "reject";
            MuxXudpQuicLabel.Text = _muxXudpQuic;

            _preferredIp = _vm.GetString("preferred_ip", "auto");
            if (string.IsNullOrWhiteSpace(_preferredIp)) _preferredIp = "auto";
            UpdateIpTypeLabel();

            _customDns = _vm.GetString("custom_dns", "1.1.1.1");
            if (string.IsNullOrWhiteSpace(_customDns)) _customDns = "1.1.1.1";
            UpdateDnsLabel();

            _tunMtu = Math.Clamp(_vm.GetInt("tun_mtu", 1500), 1200, 9000);
            UpdateMtuLabel();

            _memoryLimitMb = Math.Clamp(_vm.GetInt("memory_limit_mb", 100), 40, 512);
            MemUnlimitedSwitch.IsToggled = _vm.GetBool("memory_unlimited", false);
            UpdateMemoryButtons();

            UpdateTunnelModeUi();

            SocksAuthSwitch.IsToggled = _vm.GetBool("proxy_auth_enabled", false);
            HttpAuthHotspotSwitch.IsToggled = _vm.GetBool("http_auth_hotspot", true);
            BlockUdpSwitch.IsToggled = _vm.GetBool("block_udp", false);
            RouteOnlySwitch.IsToggled = _vm.GetBool("sniffing_route_only", true);

            _proxyUser = _vm.GetString("proxy_auth_user", "");
            _proxyPassword = _vm.GetString("proxy_auth_password", "");
            if (string.IsNullOrWhiteSpace(_proxyUser) || string.IsNullOrWhiteSpace(_proxyPassword))
            {
                GenerateNewCredentials(saveImmediately: true);
            }
            else
            {
                ProxyUserLabel.Text = _proxyUser;
                ProxyPassLabel.Text = "••••••••••••";
            }
            _initialProxyUser = _proxyUser;
            _initialProxyPassword = _proxyPassword;

            bool allowLan = _vm.GetBool("allow_lan", false);
            ProxyAddressLabel.Text = allowLan ? "0.0.0.0" : "127.0.0.1";

            int socksPort = _vm.GetInt("socks_port", Config.SocksPort);
            int httpPort = _vm.GetInt("http_port", Config.ProxyPort);
            SocksPortEntry.Text = socksPort.ToString();
            HttpPortEntry.Text = httpPort.ToString();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateTunnelModeUi()
    {
        TunnelModeLabel.Text = _useTunMode ? "TUN + Proxy" : "Только Proxy";
        if (ProxyOnlySwitch.IsToggled != !_useTunMode)
        {
            bool prev = _isLoading;
            _isLoading = true;
            ProxyOnlySwitch.IsToggled = !_useTunMode;
            _isLoading = prev;
        }
    }

    private void UpdateIpTypeLabel()
    {
        IpTypeLabel.Text = _preferredIp switch
        {
            "ipv4" => "Только IPv4",
            "ipv6" => "Только IPv6",
            _ => "Авто"
        };
    }

    private void UpdateDnsLabel()
    {
        DnsValueLabel.Text = _customDns switch
        {
            "1.1.1.1" => "Cloudflare DNS (1.1.1.1)",
            "8.8.8.8" => "Google DNS (8.8.8.8)",
            "9.9.9.9" => "Quad9 DNS (9.9.9.9)",
            "94.140.14.14" => "AdGuard DNS (94.140.14.14)",
            _ => _customDns
        };
    }

    private void UpdateMtuLabel()
    {
        MtuValueLabel.Text = _tunMtu == 1500 ? "Авто (1500)" : _tunMtu.ToString();
    }

    private void UpdateMemoryButtons()
    {
        bool unlimited = MemUnlimitedSwitch.IsToggled;
        MemorySubtitleLabel.Text = unlimited ? "Без ограничения" : $"Предел {_memoryLimitMb} MB";

        var primaryColor = Application.Current?.Resources.TryGetValue("M3Primary", out var pObj) == true && pObj is Color pc
            ? pc : Color.FromArgb("#7C4DFF");
        var onPrimaryColor = Application.Current?.Resources.TryGetValue("M3OnPrimary", out var opObj) == true && opObj is Color opc
            ? opc : Colors.White;
        var highColor = Application.Current?.Resources.TryGetValue("M3SurfaceContainerHigh", out var hObj) == true && hObj is Color hc
            ? hc : Color.FromArgb("#231D33");
        var variantText = Application.Current?.Resources.TryGetValue("M3OnSurfaceVariant", out var vtObj) == true && vtObj is Color vtc
            ? vtc : Color.FromArgb("#A89EC4");

        void StyleBtn(Border btn, Label lbl, int val)
        {
            bool active = !unlimited && _memoryLimitMb == val;
            btn.BackgroundColor = active ? primaryColor : highColor;
            lbl.TextColor = active ? onPrimaryColor : variantText;
            btn.Opacity = unlimited ? 0.5 : 1.0;
        }

        StyleBtn(Mem40Btn, Mem40Label, 40);
        StyleBtn(Mem60Btn, Mem60Label, 60);
        StyleBtn(Mem80Btn, Mem80Label, 80);
        StyleBtn(Mem100Btn, Mem100Label, 100);
    }

    private void GenerateNewCredentials(bool saveImmediately)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        _proxyUser = $"hivox_{suffix}";
        _proxyPassword = Guid.NewGuid().ToString("N")[..12];
        ProxyUserLabel.Text = _proxyUser;
        ProxyPassLabel.Text = "••••••••••••";

        if (saveImmediately)
        {
            _vm.Settings["proxy_auth_user"] = _proxyUser;
            _vm.Settings["proxy_auth_password"] = _proxyPassword;
            _vm.ApplyRuntimeSettings();
            _vm.SaveData();
        }
    }

    private static bool TryParseIntOrRange(string? raw, int minAllowed, out string normalized)
    {
        normalized = (raw ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\d+(-\d+)?$"))
            return false;
        var parts = normalized.Split('-', 2);
        if (!int.TryParse(parts[0], out int a) || a < minAllowed)
            return false;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out int b) || b < a))
            return false;
        return true;
    }

    private static bool IsValidBase64(string? raw)
    {
        string s = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return false;
        try
        {
            Convert.FromBase64String(s);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveChanges()
    {
        if (_isLoading || _hasSaved) return;
        _hasSaved = true;

        var s = _vm.Settings;
        bool modeChanged = _vm.IsTunMode != _useTunMode;

        string fragLen = TryParseIntOrRange(FragmentLengthEntry.Text, 1, out var fl)
            ? fl
            : _vm.GetString("fragment_length", "100-200");
        string fragInt = TryParseIntOrRange(FragmentIntervalEntry.Text, 0, out var fi)
            ? fi
            : _vm.GetString("fragment_interval", "10-20");
        string rawNoisePkt = (NoisePacketEntry.Text ?? "").Trim();
        string noisePkt;
        if (_noiseType == "rand")
            noisePkt = TryParseIntOrRange(rawNoisePkt, 1, out var np) ? np : _vm.GetString("noise_packet", "50-100");
        else if (_noiseType == "base64")
            noisePkt = IsValidBase64(rawNoisePkt) ? rawNoisePkt : _vm.GetString("noise_packet", "aGVsbG8=");
        else
            noisePkt = !string.IsNullOrWhiteSpace(rawNoisePkt) ? rawNoisePkt : _vm.GetString("noise_packet", "hello");

        string noiseDly = TryParseIntOrRange(NoiseDelayEntry.Text, 0, out var nd)
            ? nd
            : _vm.GetString("noise_delay", "10-15");
        int muxConc = int.TryParse(MuxConcurrencyEntry.Text?.Trim(), out int mc) && mc is >= 1 and <= 64
            ? mc
            : _vm.GetInt("mux_concurrency", 8);
        int xudpConc = int.TryParse(MuxXudpConcurrencyEntry.Text?.Trim(), out int xc) && xc is >= 1 and <= 128
            ? xc
            : _vm.GetInt("mux_xudp_concurrency", 16);
        int hp = _vm.GetInt("http_port", Config.ProxyPort);
        bool portsValid = int.TryParse(SocksPortEntry.Text?.Trim(), out int sp) && sp is >= 1024 and <= 65535 &&
                          int.TryParse(HttpPortEntry.Text?.Trim(), out hp) && hp is >= 1024 and <= 65535 && sp != hp;

        bool credsChanged = _initialProxyUser != _proxyUser || _initialProxyPassword != _proxyPassword;
        bool xrayConfigChanged =
            _vm.GetBool("fragmentation", false) != FragmentSwitch.IsToggled ||
            _vm.GetString("fragment_packets", "tlshello") != _fragmentPackets ||
            _vm.GetString("fragment_length", "100-200") != fragLen ||
            _vm.GetString("fragment_interval", "10-20") != fragInt ||
            _vm.GetBool("noises_enabled", false) != NoisesSwitch.IsToggled ||
            _vm.GetString("noise_type", "rand") != _noiseType ||
            _vm.GetString("noise_packet", "50-100") != noisePkt ||
            _vm.GetString("noise_delay", "10-15") != noiseDly ||
            _vm.GetBool("mux_enabled", true) != MuxSwitch.IsToggled ||
            _vm.GetInt("mux_concurrency", 8) != muxConc ||
            _vm.GetInt("mux_xudp_concurrency", 16) != xudpConc ||
            _vm.GetString("mux_xudp_quic", "reject") != _muxXudpQuic ||
            _vm.GetString("preferred_ip", "auto") != _preferredIp ||
            _vm.GetString("custom_dns", "1.1.1.1") != _customDns ||
            _vm.GetInt("tun_mtu", 1500) != _tunMtu ||
            _vm.GetInt("memory_limit_mb", 100) != _memoryLimitMb ||
            _vm.GetBool("memory_unlimited", false) != MemUnlimitedSwitch.IsToggled ||
            _vm.GetBool("proxy_auth_enabled", false) != SocksAuthSwitch.IsToggled ||
            _vm.GetBool("http_auth_hotspot", true) != HttpAuthHotspotSwitch.IsToggled ||
            (credsChanged && (SocksAuthSwitch.IsToggled || (HttpAuthHotspotSwitch.IsToggled && _vm.GetBool("allow_lan", false)))) ||
            _vm.GetBool("block_udp", false) != BlockUdpSwitch.IsToggled ||
            _vm.GetBool("sniffing_route_only", true) != RouteOnlySwitch.IsToggled ||
            (portsValid && (sp != _vm.GetInt("socks_port", Config.SocksPort) || hp != _vm.GetInt("http_port", Config.ProxyPort)));

        s["notif_show_speed"] = NotifSpeedSwitch.IsToggled;
        s["keep_wakelock"] = WakeLockSwitch.IsToggled;

        s["fragmentation"] = FragmentSwitch.IsToggled;
        s["fragment_packets"] = _fragmentPackets;
        s["fragment_length"] = fragLen;
        s["fragment_interval"] = fragInt;

        s["noises_enabled"] = NoisesSwitch.IsToggled;
        s["noise_type"] = _noiseType;
        s["noise_packet"] = noisePkt;
        s["noise_delay"] = noiseDly;

        s["mux_enabled"] = MuxSwitch.IsToggled;
        s["mux_concurrency"] = muxConc;
        s["mux_xudp_concurrency"] = xudpConc;
        s["mux_xudp_quic"] = _muxXudpQuic;

        s["preferred_ip"] = _preferredIp;
        s["custom_dns"] = _customDns;
        s["tun_mtu"] = _tunMtu;

        s["memory_limit_mb"] = _memoryLimitMb;
        s["memory_unlimited"] = MemUnlimitedSwitch.IsToggled;

        s["proxy_auth_enabled"] = SocksAuthSwitch.IsToggled;
        s["http_auth_hotspot"] = HttpAuthHotspotSwitch.IsToggled;
        s["block_udp"] = BlockUdpSwitch.IsToggled;
        s["sniffing_route_only"] = RouteOnlySwitch.IsToggled;
        s["proxy_auth_user"] = _proxyUser;
        s["proxy_auth_password"] = _proxyPassword;

        if (portsValid)
        {
            s["socks_port"] = sp;
            s["http_port"] = hp;
        }

        _vm.ApplyRuntimeSettings();
        _vm.SaveData();

        if (modeChanged)
        {
            if (_useTunMode)
                _vm.SetTunModeCommand.Execute(null);
            else
                _vm.SetProxyModeCommand.Execute(null);
        }
        else if (xrayConfigChanged && _vm.IsConnected)
        {
            _ = _vm.RestartActiveConnectionAsync();
        }
    }

    private async void OnTunnelModeClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("Режим туннеля", "Отмена", null,
            "TUN + Proxy (Весь трафик устройства)",
            "Только Proxy (Локальный SOCKS5/HTTP)");
        if (action == "TUN + Proxy (Весь трафик устройства)")
        {
            _useTunMode = true;
            UpdateTunnelModeUi();
        }
        else if (action == "Только Proxy (Локальный SOCKS5/HTTP)")
        {
            _useTunMode = false;
            UpdateTunnelModeUi();
        }
    }

    private void OnProxyOnlyToggled(object? sender, ToggledEventArgs e)
    {
        if (_isLoading) return;
        _useTunMode = !e.Value;
        UpdateTunnelModeUi();
    }

    private async void OnFragmentPacketsClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("Fragment Packets", "Отмена", null,
            "tlshello", "1-3", "1-5");
        if (!string.IsNullOrEmpty(action) && action != "Отмена")
        {
            _fragmentPackets = action;
            FragmentPacketsLabel.Text = _fragmentPackets;
        }
    }

    private async void OnNoiseTypeClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("Тип UDP шума (Noises)", "Отмена", null,
            "rand", "str", "base64");
        if (!string.IsNullOrEmpty(action) && action != "Отмена")
        {
            _noiseType = action;
            NoiseTypeLabel.Text = _noiseType;
            if (_noiseType == "rand") NoisePacketEntry.Text = "50-100";
            else if (_noiseType == "str") NoisePacketEntry.Text = "hello";
            else if (_noiseType == "base64") NoisePacketEntry.Text = "aGVsbG8=";
        }
    }

    private async void OnMuxXudpQuicClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("XUDP Proxy UDP443", "Отмена", null,
            "reject", "allow", "skip");
        if (!string.IsNullOrEmpty(action) && action != "Отмена")
        {
            _muxXudpQuic = action;
            MuxXudpQuicLabel.Text = _muxXudpQuic;
        }
    }

    private async void OnIpTypeClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("Тип IP", "Отмена", null,
            "Авто", "Только IPv4", "Только IPv6");
        if (action == "Авто") _preferredIp = "auto";
        else if (action == "Только IPv4") _preferredIp = "ipv4";
        else if (action == "Только IPv6") _preferredIp = "ipv6";
        else return;

        UpdateIpTypeLabel();
    }

    private async void OnDnsClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("DNS-сервер туннеля", "Отмена", null,
            "Cloudflare DNS (1.1.1.1)",
            "Google DNS (8.8.8.8)",
            "Quad9 DNS (9.9.9.9)",
            "AdGuard DNS (94.140.14.14)");
        if (action == "Cloudflare DNS (1.1.1.1)") _customDns = "1.1.1.1";
        else if (action == "Google DNS (8.8.8.8)") _customDns = "8.8.8.8";
        else if (action == "Quad9 DNS (9.9.9.9)") _customDns = "9.9.9.9";
        else if (action == "AdGuard DNS (94.140.14.14)") _customDns = "94.140.14.14";
        else return;

        UpdateDnsLabel();
    }

    private async void OnMtuClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("MTU туннеля", "Отмена", null,
            "Авто (1500)", "1420", "1400", "1360", "1280");
        if (action == "Авто (1500)") _tunMtu = 1500;
        else if (int.TryParse(action, out int mtu)) _tunMtu = mtu;
        else return;

        UpdateMtuLabel();
    }

    private void OnMem40Clicked(object? sender, EventArgs e)
    {
        _memoryLimitMb = 40;
        MemUnlimitedSwitch.IsToggled = false;
        UpdateMemoryButtons();
    }

    private void OnMem60Clicked(object? sender, EventArgs e)
    {
        _memoryLimitMb = 60;
        MemUnlimitedSwitch.IsToggled = false;
        UpdateMemoryButtons();
    }

    private void OnMem80Clicked(object? sender, EventArgs e)
    {
        _memoryLimitMb = 80;
        MemUnlimitedSwitch.IsToggled = false;
        UpdateMemoryButtons();
    }

    private void OnMem100Clicked(object? sender, EventArgs e)
    {
        _memoryLimitMb = 100;
        MemUnlimitedSwitch.IsToggled = false;
        UpdateMemoryButtons();
    }

    private void OnMemUnlimitedToggled(object? sender, ToggledEventArgs e)
    {
        if (_isLoading) return;
        UpdateMemoryButtons();
    }

    private async void OnCopyUserClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(_proxyUser);
        await HivoxDialog.ShowAlertAsync("Скопировано", "Логин скопирован в буфер обмена.", "OK");
    }

    private async void OnCopyPasswordClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(_proxyPassword);
        await HivoxDialog.ShowAlertAsync("Скопировано", "Пароль скопирован в буфер обмена.", "OK");
    }

    private async void OnCopyAddressClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(ProxyAddressLabel.Text);
        await HivoxDialog.ShowAlertAsync("Скопировано", $"Адрес {ProxyAddressLabel.Text} скопирован.", "OK");
    }

    private async void OnCopySocksPortClicked(object? sender, EventArgs e)
    {
        string port = string.IsNullOrWhiteSpace(SocksPortEntry.Text) ? Config.SocksPort.ToString() : SocksPortEntry.Text.Trim();
        await Clipboard.Default.SetTextAsync(port);
        await HivoxDialog.ShowAlertAsync("Скопировано", $"SOCKS5 порт {port} скопирован.", "OK");
    }

    private async void OnCopyHttpPortClicked(object? sender, EventArgs e)
    {
        string port = string.IsNullOrWhiteSpace(HttpPortEntry.Text) ? Config.ProxyPort.ToString() : HttpPortEntry.Text.Trim();
        await Clipboard.Default.SetTextAsync(port);
        await HivoxDialog.ShowAlertAsync("Скопировано", $"HTTP порт {port} скопирован.", "OK");
    }

    private async void OnResetProxyCredentialsClicked(object? sender, EventArgs e)
    {
        GenerateNewCredentials(saveImmediately: false);
        await HivoxDialog.ShowAlertAsync("Готово", "Сгенерированы новые логин и пароль для прокси.", "OK");
    }

    private bool TryValidateInputs(out string errorMessage)
    {
        if (!TryParseIntOrRange(FragmentLengthEntry.Text, 1, out _))
        {
            errorMessage = "Fragment Length должен быть положительным числом или корректным диапазоном (например, 100-200).";
            return false;
        }
        if (!TryParseIntOrRange(FragmentIntervalEntry.Text, 0, out _))
        {
            errorMessage = "Fragment Interval должен быть числом или корректным диапазоном (например, 10-20).";
            return false;
        }
        if (!TryParseIntOrRange(NoiseDelayEntry.Text, 0, out _))
        {
            errorMessage = "Noises Delay должен быть числом или корректным диапазоном (например, 10-15).";
            return false;
        }
        if (_noiseType == "rand" && !TryParseIntOrRange(NoisePacketEntry.Text, 1, out _))
        {
            errorMessage = "Для типа rand поле Packet должно быть положительным числом или корректным диапазоном (например, 50-100).";
            return false;
        }
        if (_noiseType == "str" && string.IsNullOrWhiteSpace(NoisePacketEntry.Text))
        {
            errorMessage = "Для типа str поле Packet не может быть пустым.";
            return false;
        }
        if (_noiseType == "base64" && !IsValidBase64(NoisePacketEntry.Text))
        {
            errorMessage = "Для типа base64 поле Packet должно содержать корректную строку Base64.";
            return false;
        }
        if (!int.TryParse(MuxConcurrencyEntry.Text?.Trim(), out int mc) || mc is < 1 or > 64)
        {
            errorMessage = "Mux Concurrency должен быть от 1 до 64.";
            return false;
        }
        if (!int.TryParse(MuxXudpConcurrencyEntry.Text?.Trim(), out int xc) || xc is < 1 or > 128)
        {
            errorMessage = "XUDP Concurrency должен быть от 1 до 128.";
            return false;
        }
        if (!int.TryParse(SocksPortEntry.Text?.Trim(), out int sp) || sp is < 1024 or > 65535)
        {
            errorMessage = "SOCKS5 порт должен быть от 1024 до 65535.";
            return false;
        }
        if (!int.TryParse(HttpPortEntry.Text?.Trim(), out int hp) || hp is < 1024 or > 65535)
        {
            errorMessage = "HTTP порт должен быть от 1024 до 65535.";
            return false;
        }
        if (sp == hp)
        {
            errorMessage = "SOCKS5 и HTTP порты не могут совпадать.";
            return false;
        }

        errorMessage = "";
        return true;
    }

    protected override bool OnBackButtonPressed()
    {
        if (_isHandlingBack) return true;
        if (!TryValidateInputs(out string error))
        {
            _ = HandleInvalidBackAsync(error);
            return true;
        }
        SaveChanges();
        return base.OnBackButtonPressed();
    }

    private async Task HandleInvalidBackAsync(string error)
    {
        _isHandlingBack = true;
        try
        {
            bool fix = await HivoxDialog.ShowConfirmAsync(
                "Некорректные значения",
                $"{error}\n\nИсправить или выйти без сохранения неверных изменений?",
                "Исправить",
                "Выйти");
            if (!fix)
            {
                _hasSaved = true;
                _isNavigating = true;
                await Shell.Current.GoToAsync("..");
            }
        }
        finally
        {
            _isHandlingBack = false;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (_isHandlingBack || _isNavigating) return;
        if (!TryValidateInputs(out string error))
        {
            await HandleInvalidBackAsync(error);
            return;
        }
        _isNavigating = true;
        SaveChanges();
        await Shell.Current.GoToAsync("..");
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        if (_isHandlingBack || _isNavigating) return;
        if (!TryValidateInputs(out string error))
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", error, "OK");
            return;
        }

        _isNavigating = true;
        SaveChanges();
        await Shell.Current.GoToAsync("..");
    }
}
