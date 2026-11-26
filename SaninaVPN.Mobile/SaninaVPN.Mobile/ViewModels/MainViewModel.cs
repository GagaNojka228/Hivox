using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Windows.Input;
using Android.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Hivox.Mobile.Models;
using Hivox.Mobile.Services;
using Hivox.Mobile.Services.PlatformAndroid;

namespace Hivox.Mobile.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly string _appDir;
    private readonly SemaphoreSlim _subUpdateSem = new(1, 1);
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _pingCts;
    private CancellationTokenSource? _netDebounceCts;
    private volatile bool _isReconnecting;
    private bool _isConnecting;
    private bool _isTransitioning;
    private bool _isPullRefreshingRunning;
    private bool _isSubPullRefreshingRunning;
    private DateTime _connectionStartTime;

    public ObservableCollection<ServerInfo> Servers { get; } = new();
    public ObservableCollection<ServerInfo> FilteredServers { get; } = new();
    public ObservableCollection<string> Subscriptions { get; } = new();
    public ObservableCollection<SubscriptionGroup> SubscriptionGroups { get; } = new();
    public Dictionary<string, SubscriptionMetadata> SubscriptionMetadataMap { get; } = new();
    public ObservableCollection<Dictionary<string, string>> ConnectionHistory { get; } = new();

    public event Action<double, double>? SpeedUpdated;

    public bool HasNoServers => Servers.Count == 0;
    public bool HasNoFilteredServers => FilteredServers.Count == 0;
    public bool HasNoSubscriptions => Subscriptions.Count == 0;
    public string ServerCountText => Servers.Count == 1 ? "1 сервер" : $"{Servers.Count} серв.";

    private ThemeMode _theme = ThemeMode.Dark;
    public ThemeMode Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                Settings["app_theme"] = value.ToString().ToLowerInvariant();
                Settings["dark_mode"] = !ThemeManager.IsEffectiveLightTheme(value);
                ThemeManager.ApplyThemeResources(value);
                SaveData();
                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(ThemeDisplayName));
                OnPropertyChanged(nameof(ConnectButtonFillBrush));
                OnPropertyChanged(nameof(PowerShieldFillBrush));
                OnPropertyChanged(nameof(PowerStrokeBrush));
                OnPropertyChanged(nameof(StatusBadgeBrush));
                OnPropertyChanged(nameof(PowerIconColor));
                OnPropertyChanged(nameof(ConnectButtonBrush));
                OnPropertyChanged(nameof(ConnectButtonGlowBrush));
                OnPropertyChanged(nameof(CheckLatencyPing));
                OnPropertyChanged(nameof(ConnectionState));
                foreach (var s in Servers)
                    s.RefreshThemeBindings();
            }
        }
    }

    public void RefreshThemeBrushes()
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ConnectButtonFillBrush));
        OnPropertyChanged(nameof(PowerShieldFillBrush));
        OnPropertyChanged(nameof(PowerStrokeBrush));
        OnPropertyChanged(nameof(StatusBadgeBrush));
        OnPropertyChanged(nameof(PowerIconColor));
        OnPropertyChanged(nameof(ConnectButtonBrush));
        OnPropertyChanged(nameof(ConnectButtonGlowBrush));
        OnPropertyChanged(nameof(ConnectionState));
        foreach (var s in Servers)
            s.RefreshThemeBindings();
    }

    public bool IsDarkTheme
    {
        get => !ThemeManager.IsEffectiveLightTheme(_theme);
        set => Theme = value ? ThemeMode.Dark : ThemeMode.Light;
    }

    public string ThemeDisplayName => _theme switch
    {
        ThemeMode.Black => "Чёрная",
        ThemeMode.Light => "Светлая",
        ThemeMode.MaterialYou => "Material You",
        _ => "Тёмная"
    };

    private ConnectionState _connectionState = ConnectionState.Disconnected;
    public ConnectionState ConnectionState
    {
        get => _connectionState;
        set
        {
            if (SetProperty(ref _connectionState, value))
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsConnecting));
                OnPropertyChanged(nameof(IsDisconnected));
                OnPropertyChanged(nameof(ConnectButtonBrush));
                OnPropertyChanged(nameof(ConnectButtonGlowBrush));
                OnPropertyChanged(nameof(ConnectButtonFillBrush));
                OnPropertyChanged(nameof(PowerShieldFillBrush));
                OnPropertyChanged(nameof(PowerStrokeBrush));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(PowerIconColor));
                OnPropertyChanged(nameof(StatusBadgeBrush));
                PlatformAndroid.HivoxTileHelper.RequestTileUpdate();
                PlatformAndroid.HivoxWidgetProvider.UpdateAllWidgets();
            }
        }
    }

    public bool IsConnected => _connectionState == ConnectionState.Connected;
    public bool IsConnecting => _connectionState is ConnectionState.Connecting or ConnectionState.Disconnecting;
    public bool IsDisconnected => _connectionState is ConnectionState.Disconnected or ConnectionState.Error;

    public string ConnectButtonText => _connectionState switch
    {
        ConnectionState.Connected => "ОТКЛЮЧИТЬ",
        ConnectionState.Connecting => "ПОДКЛЮЧЕНИЕ",
        ConnectionState.Disconnecting => "ОТКЛЮЧЕНИЕ",
        _ => "ПОДКЛЮЧИТЬ"
    };

    private static Color GetThemeColor(string key, string fallbackHex)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var val) == true && val is Color c)
            return c;
        return Color.FromArgb(fallbackHex);
    }

    public Brush ConnectButtonBrush
    {
        get
        {
            bool isLight = ThemeManager.IsEffectiveLightTheme(_theme);
            return new SolidColorBrush(_connectionState switch
            {
                ConnectionState.Connected => Color.FromArgb(isLight ? "#16A34A" : "#4ADE80"),
                ConnectionState.Connecting or ConnectionState.Disconnecting => GetThemeColor("M3Secondary", isLight ? "#6750A4" : "#B388FF"),
                ConnectionState.Error => Color.FromArgb(isLight ? "#DC2626" : "#F87171"),
                _ => GetThemeColor("M3Primary", isLight ? "#6750A4" : "#7C4DFF")
            });
        }
    }

    public Brush ConnectButtonGlowBrush
    {
        get
        {
            bool isLight = ThemeManager.IsEffectiveLightTheme(_theme);
            Color primary = GetThemeColor("M3Primary", isLight ? "#6750A4" : "#7C4DFF");
            Color secondary = GetThemeColor("M3Secondary", isLight ? "#6750A4" : "#B388FF");
            return new SolidColorBrush(_connectionState switch
            {
                ConnectionState.Connected => Color.FromArgb("#284ADE80"),
                ConnectionState.Connecting or ConnectionState.Disconnecting => secondary.WithAlpha(0.17f),
                ConnectionState.Error => Color.FromArgb("#28F87171"),
                _ => primary.WithAlpha(0.14f)
            });
        }
    }

    public Brush ConnectButtonFillBrush
    {
        get
        {
            bool isLight = ThemeManager.IsEffectiveLightTheme(_theme);
            if (_connectionState == ConnectionState.Connected)
                return new SolidColorBrush(Color.FromArgb(isLight ? "#DCFCE7" : "#0E271C"));
            if (_connectionState == ConnectionState.Error)
                return new SolidColorBrush(Color.FromArgb(isLight ? "#FEE2E2" : "#28131B"));
            return new SolidColorBrush(GetThemeColor("M3SurfaceContainer", "#201533"));
        }
    }

    public Brush PowerShieldFillBrush
    {
        get
        {
            bool isLight = ThemeManager.IsEffectiveLightTheme(_theme);
            Color primary = GetThemeColor("M3Primary", isLight ? "#6750A4" : "#7C4DFF");
            Color secondary = GetThemeColor("M3Secondary", isLight ? "#6750A4" : "#B388FF");
            return new SolidColorBrush(_connectionState switch
            {
                ConnectionState.Connected => Color.FromArgb(isLight ? "#3016A34A" : "#2E4ADE80"),
                ConnectionState.Connecting or ConnectionState.Disconnecting => secondary.WithAlpha(0.17f),
                ConnectionState.Error => Color.FromArgb("#2AF87171"),
                _ => primary.WithAlpha(0.15f)
            });
        }
    }

    public Color PowerIconColor
    {
        get
        {
            bool isLight = ThemeManager.IsEffectiveLightTheme(_theme);
            if (_connectionState == ConnectionState.Connected)
                return Color.FromArgb(isLight ? "#16A34A" : "#4ADE80");
            if (_connectionState is ConnectionState.Connecting or ConnectionState.Disconnecting)
                return GetThemeColor("M3Secondary", isLight ? "#6750A4" : "#B388FF");
            if (_connectionState == ConnectionState.Error)
                return Color.FromArgb(isLight ? "#DC2626" : "#F87171");
            return GetThemeColor("M3Secondary", isLight ? "#6750A4" : "#B388FF");
        }
    }

    public Brush PowerStrokeBrush => new SolidColorBrush(PowerIconColor);

    public Brush StatusBadgeBrush
    {
        get
        {
            bool isLight = ThemeManager.IsEffectiveLightTheme(_theme);
            if (_connectionState == ConnectionState.Connected)
                return new SolidColorBrush(Color.FromArgb(isLight ? "#DCFCE7" : "#123426"));
            return new SolidColorBrush(GetThemeColor("M3SurfaceContainerHigh", "#2A1B3D"));
        }
    }

    private ConnectionMode _connectionMode = ConnectionMode.Tun;
    public ConnectionMode ConnectionMode
    {
        get => _connectionMode;
        set
        {
            if (SetProperty(ref _connectionMode, value))
            {
                OnPropertyChanged(nameof(ConnectionModeName));
                OnPropertyChanged(nameof(IsTunMode));
                OnPropertyChanged(nameof(ModeShortLabel));
                OnPropertyChanged(nameof(ModeSubtitle));
            }
        }
    }

    public bool IsTunMode
    {
        get => ConnectionMode == ConnectionMode.Tun;
        set
        {
            var newMode = value ? ConnectionMode.Tun : ConnectionMode.Proxy;
            if (newMode != ConnectionMode)
            {
                ConnectionMode = newMode;
                SetSetting("use_tun_mode", value);
            }
        }
    }

    public List<string> ConnectionModeNames { get; } = new() { "TUN (весь трафик)", "Proxy (локальный прокси)" };

    public string ConnectionModeName
    {
        get => ConnectionMode == ConnectionMode.Tun ? ConnectionModeNames[0] : ConnectionModeNames[1];
        set
        {
            var newMode = value == ConnectionModeNames[0] ? ConnectionMode.Tun : ConnectionMode.Proxy;
            if (newMode != ConnectionMode)
            {
                ConnectionMode = newMode;
                SetSetting("use_tun_mode", newMode == ConnectionMode.Tun);
            }
        }
    }

    public string ModeShortLabel => ConnectionMode == ConnectionMode.Tun ? "TUN" : "PROXY";
    public string ModeSubtitle => ConnectionMode == ConnectionMode.Tun
        ? "Весь трафик (VPN)"
        : $"HTTP :{Config.ProxyPort} · SOCKS :{Config.SocksPort}";

    public string AppVersionBadge => $"v{AppInfo.Current.VersionString}";
    public string AppVersionLabel => $"Версия {AppInfo.Current.VersionString}";

    public string? EditingSubscriptionUrl { get; set; }

    public SubscriptionGroup? ActiveSubscriptionGroup
    {
        get
        {
            if (SubscriptionGroups.Count == 0) return null;
            var homeGroups = SubscriptionGroups.Where(g =>
                string.IsNullOrEmpty(g.Url) ||
                !SubscriptionMetadataMap.TryGetValue(g.Url, out var m) ||
                m.ShowOnHome).ToList();

            if (homeGroups.Count == 0)
                return null;

            if (SelectedServer != null)
            {
                string subUrl = SelectedServer.SubscriptionUrl ?? "";
                var match = homeGroups.FirstOrDefault(g => g.Url == subUrl);
                if (match != null) return match;
            }
            return homeGroups.FirstOrDefault();
        }
    }

    public bool HasActiveGroup => ActiveSubscriptionGroup != null;
    public bool HasMultipleGroups => SubscriptionGroups.Count > 1;

    private int _checkPing = -1;
    private bool _isCheckingConnection;
    public bool IsCheckingConnection
    {
        get => _isCheckingConnection;
        set
        {
            if (SetProperty(ref _isCheckingConnection, value))
            {
                OnPropertyChanged(nameof(CheckLatencyText));
                OnPropertyChanged(nameof(CheckStatusGlyph));
            }
        }
    }

    public int CheckLatencyPing => _checkPing != -1 ? _checkPing : SelectedServerPing;
    public bool HasCheckPingDot => !IsCheckingConnection && CheckLatencyPing >= 0;
    public string CheckStatusGlyph => IsCheckingConnection
        ? "\uE627"
        : CheckLatencyPing > 0 ? "\uE86C" : (CheckLatencyPing == 0 ? "\uE000" : "\uE640");

    public string CheckLatencyText
    {
        get
        {
            if (IsCheckingConnection) return "проверка...";
            int p = CheckLatencyPing;
            if (p > 0) return $"{p} мс";
            if (p == 0) return "недоступен";
            return "нажмите";
        }
    }

    private ServerInfo? _selectedServer;
    public ServerInfo? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (_selectedServer != value)
            {
                if (_selectedServer != null)
                    _selectedServer.IsSelected = false;

                _selectedServer = value;

                if (_selectedServer != null)
                {
                    _selectedServer.IsSelected = true;
                    Settings["selected_server_uri"] = _selectedServer.Uri;
                }
                else
                {
                    Settings["selected_server_uri"] = "";
                }

                _checkPing = -1;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedServer));
                OnPropertyChanged(nameof(ServerLabel));
                OnPropertyChanged(nameof(SelectedServerDetails));
                OnPropertyChanged(nameof(SelectedServerFlag));
                OnPropertyChanged(nameof(SelectedServerCountryBadge));
                OnPropertyChanged(nameof(SelectedServerPing));
                OnPropertyChanged(nameof(SelectedServerPingText));
                OnPropertyChanged(nameof(CheckLatencyPing));
                OnPropertyChanged(nameof(CheckLatencyText));
                OnPropertyChanged(nameof(HasCheckPingDot));
                OnPropertyChanged(nameof(CheckStatusGlyph));
                OnPropertyChanged(nameof(ActiveSubscriptionGroup));
                OnPropertyChanged(nameof(HasActiveGroup));
                SyncActiveSubscriptionMetadata();
            }
        }
    }

    public bool HasSelectedServer => SelectedServer != null;

    public string ServerLabel
    {
        get
        {
            if (SelectedServer == null) return "Выберите сервер";
            var country = !string.IsNullOrWhiteSpace(SelectedServer.CountryName) && SelectedServer.CountryName != "Unknown"
                ? SelectedServer.CountryName
                : "";
            return string.IsNullOrEmpty(country)
                ? SelectedServer.Name
                : $"{SelectedServer.Name} • {country}";
        }
    }

    public string SelectedServerDetails => SelectedServer != null
        ? SelectedServer.Details
        : "Нажмите на сервер в списке";

    public string SelectedServerFlag => SelectedServer?.Flag ?? "\U0001F310";
    public string SelectedServerCountryBadge => SelectedServer?.CountryBadge ?? "GL";
    public int SelectedServerPing => SelectedServer?.Ping ?? -1;
    public string SelectedServerPingText => SelectedServer?.PingText ?? "—";

    private string _statusText = "Не подключено";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _timerText = "00:00";
    public string TimerText
    {
        get => _timerText;
        set => SetProperty(ref _timerText, value);
    }

    private string _ipText = "";
    public string IpText
    {
        get => _ipText;
        set
        {
            if (SetProperty(ref _ipText, value))
                OnPropertyChanged(nameof(HasIpText));
        }
    }

    public bool HasIpText => !string.IsNullOrWhiteSpace(IpText);

    private string _downloadSpeedText = "0 B/s";
    public string DownloadSpeedText
    {
        get => _downloadSpeedText;
        set => SetProperty(ref _downloadSpeedText, value);
    }

    private string _uploadSpeedText = "0 B/s";
    public string UploadSpeedText
    {
        get => _uploadSpeedText;
        set => SetProperty(ref _uploadSpeedText, value);
    }

    private string _totalTransferredText = "0 B";
    public string TotalTransferredText
    {
        get => _totalTransferredText;
        set => SetProperty(ref _totalTransferredText, value);
    }

    private string _subTitle = "";
    public string SubTitle
    {
        get => _subTitle;
        set { if (SetProperty(ref _subTitle, value)) OnPropertyChanged(nameof(HasSubscriptionCard)); }
    }

    private string _subTrafficText = "";
    public string SubTrafficText
    {
        get => _subTrafficText;
        set
        {
            if (SetProperty(ref _subTrafficText, value))
            {
                OnPropertyChanged(nameof(HasSubscriptionCard));
                OnPropertyChanged(nameof(HasSubTraffic));
            }
        }
    }

    private string _subExpireText = "";
    public string SubExpireText
    {
        get => _subExpireText;
        set
        {
            if (SetProperty(ref _subExpireText, value))
            {
                OnPropertyChanged(nameof(HasSubscriptionCard));
                OnPropertyChanged(nameof(HasSubExpire));
            }
        }
    }

    private double _subTrafficRatio;
    public double SubTrafficRatio
    {
        get => _subTrafficRatio;
        set => SetProperty(ref _subTrafficRatio, value);
    }

    public bool HasSubTraffic => !string.IsNullOrEmpty(SubTrafficText);
    public bool HasSubExpire => !string.IsNullOrEmpty(SubExpireText);
    public bool HasSubscriptionCard =>
        !string.IsNullOrEmpty(SubTitle) || !string.IsNullOrEmpty(SubTrafficText) || !string.IsNullOrEmpty(SubExpireText);

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshFilteredServers();
                OnPropertyChanged(nameof(HasSearchText));
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    private string _logsText = "";
    public string LogsText
    {
        get => _logsText;
        set => SetProperty(ref _logsText, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private bool _isPinging;
    public bool IsPinging
    {
        get => _isPinging;
        set => SetProperty(ref _isPinging, value);
    }

    private bool _isPullRefreshing;
    public bool IsPullRefreshing
    {
        get => _isPullRefreshing;
        set => SetProperty(ref _isPullRefreshing, value);
    }

    private bool _isSubPullRefreshing;
    public bool IsSubPullRefreshing
    {
        get => _isSubPullRefreshing;
        set => SetProperty(ref _isSubPullRefreshing, value);
    }

    public Dictionary<string, object> Settings { get; set; } = new();

    public bool AutoSelectBest
    {
        get => GetBool("auto_select_best", true);
        set { SetSetting("auto_select_best", value); OnPropertyChanged(); }
    }

    public bool MuxEnabled
    {
        get => GetBool("mux_enabled", true);
        set { SetSetting("mux_enabled", value); OnPropertyChanged(); }
    }

    public bool FragmentationEnabled
    {
        get => GetBool("fragmentation", false);
        set { SetSetting("fragmentation", value); OnPropertyChanged(); }
    }

    public bool AutoUpdate
    {
        get => GetBool("auto_update", true);
        set
        {
            SetSetting("auto_update", value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProviderSettingsSummary));
        }
    }

    public bool AutoPing
    {
        get => GetBool("auto_ping", true);
        set { SetSetting("auto_ping", value); OnPropertyChanged(); }
    }

    public bool DisconnectOnLock
    {
        get => GetBool("disconnect_on_lock", false);
        set
        {
            Config.DisconnectOnLock = value;
            SetSetting("disconnect_on_lock", value);
            OnPropertyChanged();
        }
    }

    public bool ShowMemoryUsage
    {
        get => GetBool("show_memory_usage", false);
        set
        {
            SetSetting("show_memory_usage", value);
            OnPropertyChanged();
            UpdateMemoryUsageText();
        }
    }

    private string _memoryUsageText = "";
    public string MemoryUsageText
    {
        get => _memoryUsageText;
        set => SetProperty(ref _memoryUsageText, value);
    }

    public void UpdateMemoryUsageText()
    {
        if (!ShowMemoryUsage)
        {
            MemoryUsageText = "";
            return;
        }
        try
        {
            long bytes = GC.GetTotalMemory(false);
            long ws = Environment.WorkingSet;
            long usedMb = Math.Max(bytes, ws) / (1024 * 1024);
            MemoryUsageText = $"{Math.Max(12, usedMb)} MB";
        }
        catch
        {
            MemoryUsageText = "— MB";
        }
    }

    public string PerAppProxySummary =>
        !Config.PerAppProxyEnabled
            ? "Выкл"
            : (string.Equals(Config.PerAppProxyMode, "only_vpn", StringComparison.OrdinalIgnoreCase)
                ? (Config.PerAppPackages.Count > 0 ? $"Туннель · {Config.PerAppPackages.Count}" : "Туннель")
                : (Config.PerAppPackages.Count > 0 ? $"Обход · {Config.PerAppPackages.Count}" : "Обход"));

    public string ProviderSettingsSummary => AutoUpdate ? "Авто" : "Вручную";

    public string PingTypeLabel => Config.PingType.ToLowerInvariant() switch
    {
        "proxy" => "Real Ping",
        "http_get" => "HTTP GET",
        "http_head" => "HTTP HEAD",
        _ => "TCP"
    };

    public string SubscriptionsCountText => Subscriptions.Count == 0 ? "Нет" : $"{Subscriptions.Count}";

    public string PreferredIpLabel => GetString("preferred_ip", "auto") switch
    {
        "ipv4" => "Только IPv4",
        "ipv6" => "Только IPv6",
        _ => "Auto"
    };

    public string InboundsSummary
    {
        get
        {
            bool http = GetBool("http_enabled", true);
            bool socks = GetBool("socks_enabled", true);
            var parts = new List<string>();
            if (http) parts.Add($"HTTP :{Config.ProxyPort}");
            if (socks) parts.Add($"SOCKS :{Config.SocksPort}");
            return parts.Count > 0 ? string.Join(" · ", parts) : "Отключено";
        }
    }

    public string PingSummary => $"{Config.PingType.ToUpperInvariant()} · {Config.PingTimeout}с";

    public ICommand ToggleConnectionCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand SetTunModeCommand { get; }
    public ICommand SetProxyModeCommand { get; }
    public ICommand PingAllCommand { get; }
    public ICommand PullRefreshCommand { get; }
    public ICommand PullRefreshSubscriptionsCommand { get; }
    public ICommand TestServerCommand { get; }
    public ICommand CheckConnectionCommand { get; }
    public ICommand InspectNetworkCommand { get; }
    public ICommand ConnectToServerCommand { get; }
    public ICommand SelectServerCommand { get; }
    public ICommand CopyServerUriCommand { get; }
    public ICommand CopyIpCommand { get; }
    public ICommand DeleteServerCommand { get; }
    public ICommand DeleteSubscriptionCommand { get; }
    public ICommand ToggleGroupCommand { get; }
    public ICommand PingGroupCommand { get; }
    public ICommand UpdateGroupCommand { get; }
    public ICommand UpdateAllSubscriptionsCommand { get; }
    public ICommand SortByPingCommand { get; }
    public ICommand SortByNameCommand { get; }
    public ICommand SelectBestServerCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand RefreshLogsCommand { get; }

    public MainViewModel()
    {
        try
        {
            _appDir = PlatformHelper.AppDataDir;
            Logger.Initialize(_appDir);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("Hivox", $"MainViewModel ctor init failed: {ex}");
            _appDir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }

        Servers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoServers));
            OnPropertyChanged(nameof(ServerCountText));
        };
        FilteredServers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoFilteredServers));
        Subscriptions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoSubscriptions));
            OnPropertyChanged(nameof(SubscriptionsCountText));
        };

        ToggleConnectionCommand = new Command(async () =>
        {
            try { await ToggleConnection(); }
            catch (Exception ex) { Logger.Error($"ToggleConnection CRASH: {ex.Message}"); }
        });

        ToggleThemeCommand = new Command(ToggleTheme);
        SetTunModeCommand = new Command(async () => await SwitchModeAsync(ConnectionMode.Tun));
        SetProxyModeCommand = new Command(async () => await SwitchModeAsync(ConnectionMode.Proxy));
        PingAllCommand = new Command(async () => await PingAllServers());
        PullRefreshCommand = new Command(async () =>
        {
            if (_isPullRefreshingRunning)
            {
                await Task.Yield();
                IsPullRefreshing = false;
                return;
            }
            _isPullRefreshingRunning = true;
            IsPullRefreshing = true;
            try
            {
                await PingAllServers();
                await Task.Yield();
            }
            finally
            {
                _isPullRefreshingRunning = false;
                IsPullRefreshing = false;
            }
        });
        PullRefreshSubscriptionsCommand = new Command(async () =>
        {
            if (_isSubPullRefreshingRunning)
            {
                await Task.Yield();
                IsSubPullRefreshing = false;
                return;
            }
            _isSubPullRefreshingRunning = true;
            IsSubPullRefreshing = true;
            try
            {
                await UpdateSubscriptions();
                await Task.Yield();
            }
            finally
            {
                _isSubPullRefreshingRunning = false;
                IsSubPullRefreshing = false;
            }
        });
        TestServerCommand = new Command<ServerInfo?>(async (s) => await TestServerAsync(s ?? SelectedServer));
        CheckConnectionCommand = new Command(async () => await TestCurrentConnectionAsync());
        InspectNetworkCommand = new Command(async () => await InspectNetworkAsync());

        ConnectToServerCommand = new Command<ServerInfo>(async (server) =>
        {
            if (server == null || _isConnecting || _isTransitioning) return;
            _isTransitioning = true;
            try
            {
                bool wasConnected = IsConnected;
                SelectedServer = server;
                SaveData();
                if (wasConnected)
                {
                    Disconnect();
                    await Task.Delay(350);
                }
                await Connect();
            }
            finally
            {
                _isTransitioning = false;
            }
        });

        SelectServerCommand = new Command<ServerInfo>((server) =>
        {
            if (server == null) return;
            SelectedServer = server;
            SaveData();
        });

        CopyServerUriCommand = new Command<ServerInfo>(async (server) =>
        {
            if (server == null) return;
            CopyToClipboard("server uri", server.Uri);
            await ShowAlert("Скопировано", $"Ссылка «{server.Name}» скопирована.");
        });

        CopyIpCommand = new Command(async () =>
        {
            if (string.IsNullOrWhiteSpace(IpText)) return;
            string cleanIp = IpText.Replace("IP:", "").Trim();
            CopyToClipboard("vpn ip", cleanIp);
            await ShowAlert("Скопировано", $"IP {cleanIp} скопирован.");
        });

        DeleteServerCommand = new Command<ServerInfo>(async (server) =>
        {
            if (server == null) return;
            if (await ShowConfirm("Удалить сервер", $"Удалить «{server.Name}»?"))
                DeleteServer(server);
        });

        DeleteSubscriptionCommand = new Command<string>(async (url) =>
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!await ShowConfirm("Удалить подписку", $"Удалить подписку «{SubscriptionGroup.GetDisplayName(url)}» и все её серверы?"))
                return;
            var idx = Subscriptions.IndexOf(url);
            if (idx >= 0) DeleteSubscription(idx);
        });

        ToggleGroupCommand = new Command<SubscriptionGroup>((group) =>
        {
            if (group != null)
                group.IsExpanded = !group.IsExpanded;
        });

        PingGroupCommand = new Command<SubscriptionGroup>(async (group) =>
        {
            if (group == null || group.Servers.Count == 0) return;
            _pingCts?.Cancel();
            var pingCts = new CancellationTokenSource();
            _pingCts = pingCts;
            IsPinging = true;
            try
            {
                await NetworkUtils.PingServersParallelAsync(group.Servers.ToList(), pingCts.Token);
                if (!pingCts.IsCancellationRequested && SelectedServer != null && group.Servers.Contains(SelectedServer))
                {
                    OnPropertyChanged(nameof(SelectedServerPing));
                    OnPropertyChanged(nameof(SelectedServerPingText));
                    OnPropertyChanged(nameof(CheckLatencyPing));
                    OnPropertyChanged(nameof(CheckLatencyText));
                    OnPropertyChanged(nameof(CheckStatusGlyph));
                    OnPropertyChanged(nameof(HasCheckPingDot));
                }
                if (!pingCts.IsCancellationRequested)
                    SaveData();
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_pingCts, pingCts))
                    IsPinging = false;
            }
        });

        UpdateGroupCommand = new Command<SubscriptionGroup>(async (group) =>
        {
            if (group == null || string.IsNullOrEmpty(group.Url)) return;
            await UpdateSingleSubscriptionAsync(group.Url, group);
        });

        UpdateAllSubscriptionsCommand = new Command(async () => await UpdateSubscriptions());
        SortByPingCommand = new Command(SortByPing);
        SortByNameCommand = new Command(SortByName);
        SelectBestServerCommand = new Command(async () =>
        {
            if (Servers.Count == 0) return;
            if (!Servers.Any(s => s.Ping > 0))
                await PingAllServers();
            SelectBestServer();
        });
        ClearSearchCommand = new Command(() => SearchText = "");
        RefreshLogsCommand = new Command(() => LogsText = Logger.GetRecentLogs());

        try
        {
            LoadData();
        }
        catch (Exception ex)
        {
            Logger.Error($"MainViewModel LoadData failed: {ex.Message}");
        }
    }

    private async Task SwitchModeAsync(ConnectionMode newMode)
    {
        if (ConnectionMode == newMode || _isConnecting || _isTransitioning) return;
        _isTransitioning = true;
        try
        {
            bool wasConnected = IsConnected;
            if (wasConnected)
            {
                Disconnect();
                await Task.Delay(350);
            }
            ConnectionMode = newMode;
            SetSetting("use_tun_mode", newMode == ConnectionMode.Tun);
            if (wasConnected)
                await Connect();
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public async Task InitializeAsync()
    {
        await PlatformHelper.EnsureGeoData();

        if (Subscriptions.Count > 0 && (Servers.Count == 0 || GetBool("update_on_open", AutoUpdate) || IsAutoUpdateDue()))
            await UpdateSubscriptions(silent: true);

        if (Servers.Any(s => s.CountryName == "Unknown"))
            _ = Task.Run(() => GeoIPService.ResolveServerCountries(Servers.ToList(), _appDir));

        if (GetBool("ping_on_open", AutoPing) && Servers.Count > 0)
            await PingAllServers();

        if (AutoSelectBest && SelectedServer == null && Servers.Count > 0)
            SelectBestServer();

        if (GetBool("connect_on_open", false) && SelectedServer != null && IsDisconnected)
            await Connect();

        if (IsGeoUpdateDue())
            _ = Task.Run(() => UpdateGeoDataAsync(silent: true));

        StartAutoUpdateWatcher();
        StartNetworkChangeWatcher();
    }

    private bool _autoUpdateWatcherStarted;
    private bool _networkWatcherStarted;
    private HivoxNetworkCallback? _androidNetCallback;

    private bool IsGeoUpdateDue()
    {
        if (!GetBool("geo_auto_update", true)) return false;
        int days = Math.Clamp(GetInt("geo_update_interval_days", 7), 1, 90);
        long last = GetLong("geo_last_updated_unix", 0);
        if (last <= 0) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return (now - last) >= days * 86400L;
    }

    public async Task<(bool Ok, string Message)> UpdateGeoDataAsync(bool silent = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var (ipSha, siteSha) = await GeoDataUpdater.UpdateAllAsync(_appDir, IsConnected, progress, ct);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Settings["geo_last_updated_unix"] = now;
                OnPropertyChanged(nameof(GeoDataSummary));
                SaveData();
            });
            return (true, $"Обновлено (SHA-256 проверен: geoip {ipSha}, geosite {siteSha})");
        }
        catch (Exception ex)
        {
            Logger.Log($"GeoData update failed: {ex.Message}");
            if (!silent)
                await ShowAlert("Ошибка обновления Geo-баз", ex.Message);
            return (false, ex.Message);
        }
    }

    private void StartNetworkChangeWatcher()
    {
        if (_networkWatcherStarted) return;
        _networkWatcherStarted = true;

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                var cm = global::Android.App.Application.Context.GetSystemService(
                    global::Android.Content.Context.ConnectivityService) as ConnectivityManager;
                if (cm != null)
                {
                    _androidNetCallback = new HivoxNetworkCallback(() => ScheduleNetworkRePing("NetworkCallback"));
                    cm.RegisterDefaultNetworkCallback(_androidNetCallback);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"NetworkCallback register warning: {ex.Message}");
        }

        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
                ScheduleNetworkRePing("ConnectivityChanged");
        };
    }

    private void ScheduleNetworkRePing(string source)
    {
        if (!IsDisconnected || Servers.Count == 0)
            return;

        _netDebounceCts?.Cancel();
        var debounce = new CancellationTokenSource();
        _netDebounceCts = debounce;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000, debounce.Token).ConfigureAwait(false);
                if (debounce.IsCancellationRequested || !IsDisconnected || Servers.Count == 0) return;

                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                    return;

                Logger.Log($"Network changed ({source}) -> re-checking server latency off-UI");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (IsDisconnected && Servers.Count > 0)
                        await PingAllServers(forceRestart: true);
                });
            }
            catch (OperationCanceledException) { }
            catch { }
        }, debounce.Token);
    }

    private sealed class HivoxNetworkCallback : ConnectivityManager.NetworkCallback
    {
        private readonly Action _onChange;
        private bool _hasSeenInitial;
        public HivoxNetworkCallback(Action onChange) => _onChange = onChange;

        public override void OnAvailable(global::Android.Net.Network network)
        {
            base.OnAvailable(network);
            if (!_hasSeenInitial)
            {
                _hasSeenInitial = true;
                return;
            }
            _onChange();
        }
    }
    private bool IsAutoUpdateDue()
    {
        if (!AutoUpdate || Subscriptions.Count == 0) return false;
        int intervalHours = Math.Clamp(GetInt("update_interval", 6), 1, 24);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long oldestUpdate = Subscriptions
            .Select(u => SubscriptionMetadataMap.TryGetValue(u, out var m) ? m.LastUpdatedUnix : 0)
            .DefaultIfEmpty(0)
            .Min();
        return oldestUpdate > 0 && (now - oldestUpdate) >= intervalHours * 3600L;
    }

    private void StartAutoUpdateWatcher()
    {
        if (_autoUpdateWatcherStarted) return;
        _autoUpdateWatcherStarted = true;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10));
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (IsAutoUpdateDue() && !IsBusy)
                            await UpdateSubscriptions(silent: true);
                    });
                    if (IsGeoUpdateDue())
                        await UpdateGeoDataAsync(silent: true);
                }
                catch { }
            }
        });
    }

    public void ApplyRuntimeSettings()
    {
        int hp = GetInt("http_port", 10809);
        int sp = GetInt("socks_port", 10808);
        Config.ProxyPort = hp is > 0 and <= 65535 ? hp : 10809;
        Config.SocksPort = sp is > 0 and <= 65535 ? sp : 10808;
        Config.RequestTimeout = Math.Clamp(GetInt("request_timeout", 15), 5, 60);
        Config.PingType = GetString("ping_type", "tcp");
        Config.PingDisplay = GetString("ping_display", "time");
        Config.PingTestUrl = GetString("ping_test_url", "https://www.gstatic.com/generate_204");
        Config.PingTimeout = Math.Clamp(GetInt("ping_timeout", 3), 1, 15);
        ServerInfo.PingDisplayMode = Config.PingDisplay;

        int mtu = GetInt("tun_mtu", 1500);
        Config.TunMtu = mtu is >= 1200 and <= 9000 ? mtu : 1500;
        Config.CustomDns = GetString("custom_dns", "1.1.1.1");
        Config.SendHwid = GetBool("send_hwid", true);
        string ua = GetString("custom_user_agent", "v2rayNG/1.9.34");
        Config.CustomUserAgent = string.IsNullOrWhiteSpace(ua) || ua == "Hivox/2.0 (Android; Xray-core/26.3)"
            ? "v2rayNG/1.9.34"
            : ua;
        Config.AutoReconnectEnabled = GetBool("auto_reconnect", true);
        Config.BypassLan = GetBool("bypass_lan", true);
        Config.BypassRu = GetBool("bypass_ru", true);
        Config.AllowLan = GetBool("allow_lan", false);
        Config.SniffingRouteOnly = GetBool("sniffing_route_only", true);
        Config.BlockUdp = GetBool("block_udp", false);
        Config.ProxyAuthEnabled = GetBool("proxy_auth_enabled", false);
        Config.HttpAuthOnHotspot = GetBool("http_auth_hotspot", true);
        Config.HttpEnabled = GetBool("http_enabled", true);
        Config.SocksEnabled = GetBool("socks_enabled", true);
        Config.DisconnectOnLock = GetBool("disconnect_on_lock", false);
        bool credsGenerated = false;
        string savedUser = GetString("proxy_auth_user", "");
        if (string.IsNullOrWhiteSpace(savedUser))
        {
            savedUser = $"hivox_{Guid.NewGuid().ToString("N")[..8]}";
            Settings["proxy_auth_user"] = savedUser;
            credsGenerated = true;
        }
        Config.ProxyAuthUser = savedUser;

        string savedPass = GetString("proxy_auth_password", "");
        if (string.IsNullOrWhiteSpace(savedPass))
        {
            savedPass = GetString("proxy_auth_pass", "");
            if (string.IsNullOrWhiteSpace(savedPass))
                savedPass = Guid.NewGuid().ToString("N")[..12];
            Settings["proxy_auth_password"] = savedPass;
            credsGenerated = true;
        }
        Config.ProxyAuthPassword = savedPass;
        if (credsGenerated)
            SaveData();

        Config.FragmentPackets = GetString("fragment_packets", "tlshello");
        Config.FragmentLength = GetString("fragment_length", "100-200");
        Config.FragmentInterval = GetString("fragment_interval", "10-20");

        Config.NoisesEnabled = GetBool("noises_enabled", false);
        Config.NoiseType = GetString("noise_type", "rand");
        Config.NoisePacket = GetString("noise_packet", "50-100");
        Config.NoiseDelay = GetString("noise_delay", "10-15");

        Config.MuxConcurrency = Math.Clamp(GetInt("mux_concurrency", 8), 1, 64);
        Config.MuxXudpConcurrency = Math.Clamp(GetInt("mux_xudp_concurrency", 16), 1, 128);
        Config.MuxXudpQuic = GetString("mux_xudp_quic", "reject");

        Config.MemoryLimitMb = Math.Clamp(GetInt("memory_limit_mb", 100), 40, 512);
        Config.MemoryUnlimited = GetBool("memory_unlimited", false);
        Config.KeepWakeLock = GetBool("keep_wakelock", true);
        Config.NotifShowSpeed = GetBool("notif_show_speed", false);
        Config.NotifShowServer = GetBool("notif_show_server", true);

        Config.PerAppProxyEnabled = GetBool("per_app_proxy_enabled", false);
        Config.PerAppProxyMode = GetString("per_app_proxy_mode", "bypass");
        string pkgsCsv = GetString("per_app_packages", "");
        Config.PerAppPackages = new HashSet<string>(
            pkgsCsv.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(p => p.Trim())
                   .Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        UpdateMemoryUsageText();

        foreach (var s in Servers)
            s.RefreshPingDisplay();

        OnPropertyChanged(nameof(InboundsSummary));
        OnPropertyChanged(nameof(ModeSubtitle));
        OnPropertyChanged(nameof(PreferredIpLabel));
        OnPropertyChanged(nameof(PingSummary));
        OnPropertyChanged(nameof(PingTypeLabel));
        OnPropertyChanged(nameof(PerAppProxySummary));
        OnPropertyChanged(nameof(ProviderSettingsSummary));
        OnPropertyChanged(nameof(SubscriptionsCountText));
        OnPropertyChanged(nameof(DisconnectOnLock));
        OnPropertyChanged(nameof(ShowMemoryUsage));
        OnPropertyChanged(nameof(SelectedServerPingText));
        OnPropertyChanged(nameof(BatteryOptimizationSummary));
        OnPropertyChanged(nameof(AppLanguageSummary));
        OnPropertyChanged(nameof(GeoDataSummary));

        try
        {
            var prefs = global::Android.App.Application.Context
                .GetSharedPreferences("sanina_vpn", global::Android.Content.FileCreationMode.Private);
            var editor = prefs?.Edit();
            if (editor != null)
            {
                editor.PutBoolean("connect_on_boot", GetBool("connect_on_boot", false));
                editor.Apply();
            }
        }
        catch { }

        PlatformAndroid.HivoxTileHelper.RequestTileUpdate();
        PlatformAndroid.HivoxWidgetProvider.UpdateAllWidgets();

        if (PlatformAndroid.SaninaVpnService.IsRunning)
        {
            PlatformAndroid.SaninaVpnService.ApplyRuntimeKeepAwake();
            PlatformAndroid.SaninaVpnService.RefreshRuntimeNotification();
        }
    }

    public string BatteryOptimizationSummary
    {
        get
        {
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(23))
                {
                    var ctx = global::Android.App.Application.Context;
                    var pm = ctx.GetSystemService(global::Android.Content.Context.PowerService) as global::Android.OS.PowerManager;
                    if (pm != null && pm.IsIgnoringBatteryOptimizations(ctx.PackageName))
                        return "Без ограничений";
                    return "Ограничено";
                }
                return "Не требуется";
            }
            catch
            {
                return "Проверить";
            }
        }
    }

    public string AppLanguageSummary
    {
        get
        {
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    var ctx = global::Android.App.Application.Context;
                    var lm = ctx.GetSystemService(global::Android.Content.Context.LocaleService) as global::Android.App.LocaleManager;
                    var locales = lm?.ApplicationLocales;
                    if (locales == null || locales.IsEmpty)
                        return "Системный";

                    string lang = locales.Get(0)?.Language?.ToLowerInvariant() ?? "";
                    return lang switch
                    {
                        "ru" => "Русский",
                        "en" => "English",
                        _ => locales.Get(0)?.DisplayLanguage ?? "Системный"
                    };
                }

                string uiLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                return uiLang == "ru" ? "Русский" : "English";
            }
            catch
            {
                return "Системный";
            }
        }
    }

    public string GeoDataSummary
    {
        get
        {
            if (!GetBool("geo_auto_update", true))
                return "Вручную";
            int days = Math.Clamp(GetInt("geo_update_interval_days", 7), 1, 90);
            return $"Авто · {days} дн.";
        }
    }

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _saveDebounceCts;

    public void SaveData()
    {
        var subs = Subscriptions.ToList();
        var srvs = Servers.ToList();
        var hist = ConnectionHistory.ToList();
        var st = new Dictionary<string, object>(Settings);
        var meta = new Dictionary<string, SubscriptionMetadata>(SubscriptionMetadataMap);

        _saveDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token).ConfigureAwait(false);
                if (cts.IsCancellationRequested) return;

                await _saveLock.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    DataManager.SaveAppData(_appDir, subs, srvs, hist, st, meta);
                }
                finally
                {
                    _saveLock.Release();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Log($"Background SaveData warning: {ex.Message}");
            }
        }, cts.Token);
    }

    public bool GetBool(string key, bool defaultValue = false) =>
        Settings.TryGetValue(key, out var val) && val is bool b ? b : defaultValue;

    public string GetString(string key, string defaultValue = "") =>
        Settings.TryGetValue(key, out var val) && val is string s ? s : defaultValue;

    public int GetInt(string key, int defaultValue = 0)
    {
        if (!Settings.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            int i => i,
            long l => (int)l,
            _ => defaultValue
        };
    }

    public long GetLong(string key, long defaultValue = 0)
    {
        if (!Settings.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            long l => l,
            int i => i,
            _ => long.TryParse(val?.ToString(), out long parsed) ? parsed : defaultValue
        };
    }

    public async Task ImportScannedQrAsync(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string trimmed = raw.Trim();

        if (trimmed.StartsWith("hivox://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("saninavpn://", StringComparison.OrdinalIgnoreCase))
        {
            int schemeLen = trimmed.StartsWith("hivox://", StringComparison.OrdinalIgnoreCase) ? 8 : 12;
            var parsed = global::Android.Net.Uri.Parse(trimmed);
            string? paramUrl = parsed?.GetQueryParameter("url");
            string target = !string.IsNullOrWhiteSpace(paramUrl)
                ? paramUrl
                : trimmed[schemeLen..].TrimStart('/');

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                await AddSubscription(target);
            else if (!string.IsNullOrWhiteSpace(target))
                await ImportMassTextAsync(target);
            return;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await AddSubscription(trimmed);
            return;
        }

        if (SubscriptionManager.ValidateServerUri(trimmed))
        {
            await AddServerUri(trimmed);
            return;
        }

        await ImportMassTextAsync(trimmed);
    }

    public void SetSetting(string key, object value)
    {
        Settings[key] = value;
        SaveData();
    }

    public void ToggleTheme()
    {
        Theme = _theme switch
        {
            ThemeMode.Dark => ThemeMode.Black,
            ThemeMode.Black => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.MaterialYou,
            _ => ThemeMode.Dark
        };
    }

    public void SelectServer(int index)
    {
        SelectedServer = index >= 0 && index < Servers.Count ? Servers[index] : null;
    }

    public void SelectBestServer()
    {
        var best = Servers
            .Where(s => s.Ping > 0)
            .OrderBy(s => s.Ping)
            .FirstOrDefault() ?? Servers.FirstOrDefault();

        if (best != null)
        {
            SelectedServer = best;
            SaveData();
        }
    }

    public async Task TestServerAsync(ServerInfo? server)
    {
        if (server == null)
        {
            await ShowAlert("Проверка", "Выберите сервер для проверки.");
            return;
        }
        string prev = StatusText;
        StatusText = $"Проверка {server.Name}...";
        int ping = await NetworkUtils.PingServerAsync(server, useProxyIfAvailable: IsConnected);
        server.Ping = ping;
        if (server == SelectedServer)
        {
            OnPropertyChanged(nameof(SelectedServerPing));
            OnPropertyChanged(nameof(SelectedServerPingText));
        }
        StatusText = ping > 0 ? $"{server.Name} — {ping} ms" : $"{server.Name} — недоступен";
        await Task.Delay(1800);
        if (StatusText.StartsWith(server.Name))
            StatusText = IsConnected ? "Подключено" : prev;
    }

    public async Task TestCurrentConnectionAsync()
    {
        if (IsCheckingConnection) return;
        var target = SelectedServer;
        if (target == null)
        {
            await ShowAlert("Проверка", "Выберите сервер в списке подписки или во вкладке «Серверы».");
            return;
        }

        IsCheckingConnection = true;
        try
        {
            int ping = await NetworkUtils.PingServerAsync(target, useProxyIfAvailable: IsConnected);
            target.Ping = ping;
            if (SelectedServer == target)
                _checkPing = ping;
            OnPropertyChanged(nameof(SelectedServerPing));
            OnPropertyChanged(nameof(SelectedServerPingText));
            OnPropertyChanged(nameof(CheckLatencyPing));
            OnPropertyChanged(nameof(CheckLatencyText));
            OnPropertyChanged(nameof(CheckStatusGlyph));
            OnPropertyChanged(nameof(HasCheckPingDot));

            if (IsConnected)
                _ = Task.Run(() => DetectIp(CancellationToken.None));

            SaveData();
        }
        finally
        {
            IsCheckingConnection = false;
        }
    }

    public async Task InspectNetworkAsync()
    {
        try
        {
            string infoLine;
            string? detectedIp = null;
            HttpClient CreateClient()
            {
                if (IsConnected)
                {
                    var handler = new HttpClientHandler
                    {
                        Proxy = Config.CreateLocalHttpProxy(),
                        UseProxy = true
                    };
                    return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
                }
                return new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            }

            using (var client = CreateClient())
            {
                var resp = await client.GetStringAsync("https://api.ipify.org?format=json");
                detectedIp = JObject.Parse(resp)["ip"]?.ToString();
            }

            if (string.IsNullOrWhiteSpace(detectedIp))
            {
                await ShowAlert("Проверка сети", "Не удалось определить внешний IP-адрес.");
                return;
            }

            if (IsConnected)
                MainThread.BeginInvokeOnMainThread(() => IpText = detectedIp);

            string details = $"IP: {detectedIp}\nРежим: {ModeShortLabel}";
            try
            {
                using var geoClient = CreateClient();
                var geoJson = JObject.Parse(await geoClient.GetStringAsync($"https://ipwho.is/{detectedIp}"));
                if (geoJson["success"]?.ToObject<bool>() == true)
                {
                    string country = geoJson["country"]?.ToString() ?? "";
                    string city = geoJson["city"]?.ToString() ?? "";
                    string isp = geoJson["connection"]?["isp"]?.ToString() ?? "";
                    details = $"IP: {detectedIp}\nЛокация: {(string.IsNullOrEmpty(city) ? country : $"{country}, {city}")}";
                    if (!string.IsNullOrEmpty(isp)) details += $"\nПровайдер: {isp}";
                    details += $"\nРежим: {ModeShortLabel} ({(IsConnected ? "VPN активен" : "Напрямую")})";
                }
            }
            catch { }

            infoLine = details;
            bool copy = await HivoxDialog.ShowConfirmAsync("Сведения о сети", infoLine, "Скопировать IP", "Закрыть");
            if (copy)
                CopyToClipboard("external ip", detectedIp);
        }
        catch (Exception ex)
        {
            await ShowAlert("Проверка сети", $"Ошибка запроса: {ex.Message}");
        }
    }

    public async Task SwitchActiveSubscriptionAsync(SubscriptionGroup? group)
    {
        if (_isConnecting || _isTransitioning || group == null || group.Servers.Count == 0) return;
        var target = group.Servers
            .Where(s => s.Ping > 0)
            .OrderBy(s => s.Ping)
            .FirstOrDefault() ?? group.Servers.FirstOrDefault();

        if (target == null) return;
        _isTransitioning = true;
        try
        {
            bool wasConnected = IsConnected;
            SelectedServer = target;
            SaveData();
            if (wasConnected)
            {
                Disconnect();
                await Task.Delay(350);
                await Connect();
            }
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public async Task PingAllServers(bool forceRestart = false)
    {
        if (Servers.Count == 0) return;
        if (IsPinging && !forceRestart) return;

        _pingCts?.Cancel();
        var pingCts = new CancellationTokenSource();
        _pingCts = pingCts;

        IsPinging = true;
        string prevStatus = StatusText;
        if (IsDisconnected) StatusText = "Проверка пинга...";

        try
        {
            var list = Servers.ToList();
            await NetworkUtils.PingServersParallelAsync(list, pingCts.Token);
            if (pingCts.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (AutoSelectBest && IsDisconnected)
                    SelectBestServer();

                OnPropertyChanged(nameof(SelectedServerPing));
                OnPropertyChanged(nameof(SelectedServerPingText));
                OnPropertyChanged(nameof(CheckLatencyPing));
                OnPropertyChanged(nameof(CheckLatencyText));
                OnPropertyChanged(nameof(CheckStatusGlyph));
                OnPropertyChanged(nameof(HasCheckPingDot));
                if (IsDisconnected)
                    StatusText = "Пинг обновлён";
            });
            SaveData();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_pingCts, pingCts))
            {
                IsPinging = false;
                if (IsDisconnected && StatusText == "Проверка пинга...")
                    StatusText = prevStatus;
            }
        }
    }

    public void SortByPing()
    {
        var sorted = Servers.OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping).ToList();
        ReplaceServers(sorted);
    }

    public void SortByName()
    {
        var sorted = Servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ReplaceServers(sorted);
    }

    private void ReplaceServers(List<ServerInfo> sorted)
    {
        Servers.Clear();
        foreach (var s in sorted)
            Servers.Add(s);
        RefreshFilteredServers();
        RebuildGroups();
        SaveData();
    }

    public void RefreshFilteredServers()
    {
        var query = (SearchText ?? "").Trim();
        IEnumerable<ServerInfo> source = Servers;
        if (!string.IsNullOrEmpty(query))
        {
            source = Servers.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.CountryName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        FilteredServers.Clear();
        foreach (var s in source)
            FilteredServers.Add(s);
    }

    private void ApplySubscriptionMetadata(SubscriptionMetadata? meta)
    {
        if (meta == null) return;
        SubTitle = meta.ProfileTitle;
        SubTrafficText = meta.FormattedTraffic;
        SubExpireText = meta.FormattedExpire;
        SubTrafficRatio = meta.TrafficRatio;
    }

    public void RebuildGroups()
    {
        var expandedState = SubscriptionGroups.ToDictionary(g => g.Url, g => g.IsExpanded);
        SubscriptionGroups.Clear();

        foreach (var url in Subscriptions)
        {
            SubscriptionMetadataMap.TryGetValue(url, out var meta);
            string displayName = meta != null && !string.IsNullOrWhiteSpace(meta.CustomName)
                ? meta.CustomName
                : (meta != null && !string.IsNullOrWhiteSpace(meta.ProfileTitle)
                    ? meta.ProfileTitle
                    : SubscriptionGroup.GetDisplayName(url));

            string intervalText = meta?.UpdateIntervalText ?? "";
            if (meta?.UseCustomInterval == true && meta.CustomIntervalMinutes > 0)
            {
                intervalText = meta.CustomIntervalMinutes < 60
                    ? $"{meta.CustomIntervalMinutes}м"
                    : $"{meta.CustomIntervalMinutes / 60}ч";
            }

            bool expanded = meta?.AlwaysExpanded == true || (expandedState.TryGetValue(url, out var exp) ? exp : true);

            var group = new SubscriptionGroup
            {
                Url = url,
                DisplayName = displayName,
                IsExpanded = expanded,
                TrafficText = meta?.FormattedTraffic ?? "",
                ExpireText = meta?.FormattedExpire ?? "",
                ShortExpireText = meta?.ShortExpireText ?? "",
                TrafficRatio = meta?.TrafficRatio ?? 0,
                UpdateIntervalText = intervalText,
                LastUpdatedText = meta?.LastUpdatedText ?? "",
                SupportUrl = meta?.SupportUrl ?? "",
                WebPageUrl = meta?.WebPageUrl ?? ""
            };
            foreach (var s in Servers.Where(s => s.SubscriptionUrl == url))
                group.Servers.Add(s);

            if (group.Servers.Count > 0)
                SubscriptionGroups.Add(group);
        }

        var unassigned = Servers.Where(s =>
            string.IsNullOrEmpty(s.SubscriptionUrl) || !Subscriptions.Contains(s.SubscriptionUrl)).ToList();

        if (unassigned.Count > 0)
        {
            var group = new SubscriptionGroup
            {
                Url = "",
                DisplayName = Subscriptions.Count > 0 ? "Ручные серверы" : "Серверы",
                IsExpanded = expandedState.TryGetValue("", out var exp) ? exp : true
            };
            foreach (var s in unassigned)
                group.Servers.Add(s);
            SubscriptionGroups.Add(group);
        }

        SyncActiveSubscriptionMetadata();
        OnPropertyChanged(nameof(ActiveSubscriptionGroup));
        OnPropertyChanged(nameof(HasActiveGroup));
        OnPropertyChanged(nameof(HasMultipleGroups));
    }

    private void SyncActiveSubscriptionMetadata()
    {
        var active = ActiveSubscriptionGroup;
        if (active != null && !string.IsNullOrEmpty(active.Url) &&
            SubscriptionMetadataMap.TryGetValue(active.Url, out var activeMeta) &&
            (!string.IsNullOrEmpty(activeMeta.ProfileTitle) || activeMeta.HasTrafficInfo || activeMeta.ExpireUnix > 0))
        {
            ApplySubscriptionMetadata(activeMeta);
            return;
        }

        var firstMeta = SubscriptionMetadataMap.Values.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.ProfileTitle) || m.HasTrafficInfo || m.ExpireUnix > 0);
        if (firstMeta != null)
            ApplySubscriptionMetadata(firstMeta);
        else
        {
            SubTitle = "";
            SubTrafficText = "";
            SubExpireText = "";
            SubTrafficRatio = 0;
        }
    }

    public async Task RestartActiveConnectionAsync()
    {
        if (!IsConnected || _isConnecting || _isTransitioning) return;
        _isTransitioning = true;
        try
        {
            Disconnect();
            await Task.Delay(350);
            await Connect();
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public async Task ToggleConnection()
    {
        if (_isConnecting || _isTransitioning) return;

        if (ConnectionState == ConnectionState.Connected)
            Disconnect();
        else if (ConnectionState is ConnectionState.Disconnected or ConnectionState.Error)
            await Connect();
    }

    public void DisconnectFromExternal()
    {
        if (_isConnecting || _isTransitioning)
        {
            Disconnect();
            return;
        }
        if (ConnectionState != ConnectionState.Disconnected ||
            PlatformAndroid.SaninaVpnService.IsRunning ||
            XrayProcessManager.IsRunning)
        {
            Disconnect();
        }
    }

    public async Task ConnectHeadlessAsync()
    {
        if (_isConnecting || _isTransitioning) return;
        if (ConnectionState is ConnectionState.Disconnected or ConnectionState.Error)
            await Connect();
    }

    public async Task<bool> ConnectFromBootServiceAsync(
        PlatformAndroid.SaninaVpnService service,
        CancellationToken serviceToken)
    {
        if (_isConnecting || _isTransitioning) return false;
        _isConnecting = true;

        try
        {
            var server = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (SelectedServer == null && Servers.Count > 0)
                    SelectBestServer();
                return SelectedServer;
            });

            if (server == null)
            {
                Logger.Log("ConnectFromBootServiceAsync: no server selected/available");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ConnectionState = ConnectionState.Disconnected;
                    StatusText = "Не подключено";
                });
                return false;
            }

            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
            var token = _cts.Token;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ConnectionState = ConnectionState.Connecting;
                StatusText = "Подключение...";
                IpText = "";
                DownloadSpeedText = "0 B/s";
                UploadSpeedText = "0 B/s";
                TotalTransferredText = "0 B";
            });

            await PlatformHelper.EnsureGeoData();

            string preferredIp = GetString("preferred_ip", "auto");
            bool muxEnabled = GetBool("mux_enabled", true);
            bool fragmentation = GetBool("fragmentation", false);
            bool httpEnabled = GetBool("http_enabled", true);
            bool socksEnabled = GetBool("socks_enabled", true);

            var config = ConfigBuilder.BuildConfig(
                server,
                gameMode: false,
                connectionMode: ConnectionMode.Tun,
                muxEnabled: muxEnabled,
                preferredIp: preferredIp,
                fragmentation: fragmentation,
                httpEnabled: httpEnabled,
                socksEnabled: socksEnabled);

            if (config == null)
            {
                Logger.Error("ConnectFromBootServiceAsync: BuildConfig returned null");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ConnectionState = ConnectionState.Error;
                    StatusText = "Ошибка конфигурации";
                });
                return false;
            }

            string configPath = PlatformHelper.ConfigPath;
            File.WriteAllText(configPath, config.ToString(Formatting.Indented));

            int tunFd = await service.EstablishTunForBootAsync(
                $"{server.Flag} {server.Name}",
                preferredIp == "ipv6",
                token);

            bool started = await XrayProcessManager.StartAsync(configPath, PlatformHelper.XrayPath, tunFd);
            if (!started)
            {
                Logger.Error($"ConnectFromBootServiceAsync: Xray start failed: {XrayProcessManager.LastError}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ConnectionState = ConnectionState.Error;
                    StatusText = "Ошибка запуска";
                });
                return false;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (ConnectionMode != ConnectionMode.Tun)
                {
                    ConnectionMode = ConnectionMode.Tun;
                    SetSetting("use_tun_mode", true);
                }

                ConnectionState = ConnectionState.Connected;
                _connectionStartTime = DateTime.Now;
                StatusText = "Подключено";
                TimerText = "00:00";

                ConnectionHistory.Insert(0, new Dictionary<string, string>
                {
                    ["server"] = server.Name,
                    ["mode"] = ModeShortLabel,
                    ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                while (ConnectionHistory.Count > Config.MaxHistoryEntries)
                    ConnectionHistory.RemoveAt(ConnectionHistory.Count - 1);

                SaveData();
                PlatformAndroid.HivoxTileHelper.RequestTileUpdate();
            });

            _ = Task.Run(() => HealthCheckLoop(token), token);
            _ = Task.Run(() => DetectIp(token), token);
            _ = Task.Run(() => MonitorTrafficLoop(token), token);

            Logger.Log($"Connected headlessly (boot/tile) to {server.Name}");
            return true;
        }
        catch (OperationCanceledException)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ConnectionState = ConnectionState.Disconnected;
                StatusText = "Не подключено";
            });
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"ConnectFromBootServiceAsync error: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ConnectionState = ConnectionState.Error;
                StatusText = "Ошибка подключения";
            });
            return false;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    public void OnSystemVpnStopped(string reason, long sessionGen = 0)
    {
        if (sessionGen > 0 && sessionGen < PlatformAndroid.SaninaVpnService.CurrentRequestedSessionGen)
        {
            Logger.Log($"Ignored stale VPN stop broadcast (reason={reason}, gen={sessionGen}, current={PlatformAndroid.SaninaVpnService.CurrentRequestedSessionGen})");
            return;
        }
        if (ConnectionState == ConnectionState.Disconnected && reason == "user")
            return;

        Logger.Log($"System VPN stopped ({reason})");
        _cts?.Cancel();
        XrayProcessManager.Stop();
        ConnectionState = ConnectionState.Disconnected;
        StatusText = reason switch
        {
            "revoked" => "VPN отключён системой",
            "screen_lock" => "Отключено при блокировке",
            _ => "Не подключено"
        };
        TimerText = "00:00";
        IpText = "";
        DownloadSpeedText = "0 B/s";
        UploadSpeedText = "0 B/s";
        TotalTransferredText = "0 B";
    }

    public void SyncWithSystemState()
    {
        if (ConnectionState == ConnectionState.Connected && !_isConnecting && !_isTransitioning &&
            ((ConnectionMode == ConnectionMode.Tun && !PlatformAndroid.SaninaVpnService.IsRunning) || !XrayProcessManager.IsRunning))
        {
            OnSystemVpnStopped("user", PlatformAndroid.SaninaVpnService.CurrentRequestedSessionGen);
        }
    }

    private async Task Connect()
    {
        if (_isConnecting) return;

        if (SelectedServer == null)
        {
            if (Servers.Count > 0)
                SelectBestServer();
            else
            {
                await ShowAlert("Нет серверов", "Добавьте подписку или сервер, чтобы подключиться.");
                return;
            }
        }

        if (SelectedServer == null)
        {
            await ShowAlert("Ошибка", "Выберите сервер для подключения.");
            return;
        }

        var server = SelectedServer;
        _isConnecting = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        ConnectionState = ConnectionState.Connecting;
        StatusText = "Подключение...";
        IpText = "";
        DownloadSpeedText = "0 B/s";
        UploadSpeedText = "0 B/s";
        TotalTransferredText = "0 B";

        try
        {
            await PlatformHelper.EnsureGeoData();

            string preferredIp = GetString("preferred_ip", "auto");
            bool muxEnabled = GetBool("mux_enabled", true);
            bool fragmentation = GetBool("fragmentation", false);
            bool httpEnabled = GetBool("http_enabled", true);
            bool socksEnabled = GetBool("socks_enabled", true);

            var config = ConfigBuilder.BuildConfig(
                server,
                gameMode: false,
                connectionMode: ConnectionMode,
                muxEnabled: muxEnabled,
                preferredIp: preferredIp,
                fragmentation: fragmentation,
                httpEnabled: httpEnabled,
                socksEnabled: socksEnabled);

            if (config == null)
            {
                await ShowAlert("Ошибка конфигурации", $"Не удалось создать конфиг для {server.Protocol}.");
                ConnectionState = ConnectionState.Error;
                StatusText = "Ошибка конфигурации";
                return;
            }

            Logger.Log($"Connecting: mode={ModeShortLabel}, rawJson={!string.IsNullOrWhiteSpace(server.RawJsonConfig)}, proto={server.Protocol}");

            string configPath = PlatformHelper.ConfigPath;
            File.WriteAllText(configPath, config.ToString(Formatting.Indented));

            int? tunFd = null;
            if (ConnectionMode == ConnectionMode.Tun)
            {
                var context = Android.App.Application.Context;
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                var prepareIntent = VpnService.Prepare(activity ?? context);
                if (prepareIntent != null)
                {
                    if (activity == null)
                    {
                        await ShowAlert("Ошибка", "Не удалось запросить разрешение VPN.");
                        ConnectionState = ConnectionState.Error;
                        StatusText = "Ошибка VPN";
                        return;
                    }

                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    PlatformAndroid.MainActivity.VpnPermissionTask?.TrySetCanceled();
                    PlatformAndroid.MainActivity.VpnPermissionTask = tcs;
                    activity.StartActivityForResult(prepareIntent, PlatformAndroid.MainActivity.VpnRequestCode);

                    var permTimeout = Task.Delay(TimeSpan.FromSeconds(60), token);
                    if (await Task.WhenAny(tcs.Task, permTimeout) != tcs.Task ||
                        !tcs.Task.IsCompletedSuccessfully || !tcs.Task.Result)
                    {
                        await ShowAlert("Нет разрешения", "Разрешение на использование VPN не выдано.");
                        ConnectionState = ConnectionState.Error;
                        StatusText = "Ошибка VPN";
                        return;
                    }
                }

                PlatformAndroid.SaninaVpnService.StartVpn(
                    context,
                    $"{server.Flag} {server.Name}",
                    preferredIp == "ipv6");

                try
                {
                    var fdTask = PlatformAndroid.SaninaVpnService.TunFdReady?.Task
                        ?? throw new Exception("TunFdReady не инициализирован");

                    if (await Task.WhenAny(fdTask, Task.Delay(TimeSpan.FromSeconds(8), token)) != fdTask)
                        throw new TimeoutException("Тайм-аут создания TUN-интерфейса");

                    tunFd = await fdTask;
                }
                catch (Exception ex)
                {
                    Logger.Error($"TUN setup failed: {ex.Message}");
                    PlatformAndroid.SaninaVpnService.StopVpn(context, fromUi: true);
                    await ShowAlert("Ошибка TUN", ex.Message);
                    ConnectionState = ConnectionState.Error;
                    StatusText = "Ошибка подключения";
                    return;
                }
            }

            bool started = await XrayProcessManager.StartAsync(configPath, PlatformHelper.XrayPath, tunFd);
            if (!started)
            {
                string err = XrayProcessManager.LastError;
                Logger.Error($"Xray start failed: {err}");
                if (ConnectionMode == ConnectionMode.Tun)
                    PlatformAndroid.SaninaVpnService.StopVpn(Android.App.Application.Context, fromUi: true);
                await ShowAlert("Ошибка запуска Xray", string.IsNullOrEmpty(err) ? "Неизвестная ошибка" : err);
                ConnectionState = ConnectionState.Error;
                StatusText = "Ошибка запуска";
                return;
            }

            ConnectionState = ConnectionState.Connected;
            _connectionStartTime = DateTime.Now;
            StatusText = "Подключено";
            TimerText = "00:00";

            ConnectionHistory.Insert(0, new Dictionary<string, string>
            {
                ["server"] = server.Name,
                ["mode"] = ModeShortLabel,
                ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            while (ConnectionHistory.Count > Config.MaxHistoryEntries)
                ConnectionHistory.RemoveAt(ConnectionHistory.Count - 1);

            _ = Task.Run(() => HealthCheckLoop(token), token);
            _ = Task.Run(() => DetectIp(token), token);
            _ = Task.Run(() => MonitorTrafficLoop(token), token);

            Logger.Log($"Connected to {server.Name} ({ModeShortLabel})");
            SaveData();
        }
        catch (Exception ex)
        {
            Logger.Error($"Connection error: {ex.Message}");
            XrayProcessManager.Stop();
            if (ConnectionMode == ConnectionMode.Tun || PlatformAndroid.SaninaVpnService.IsRunning)
            {
                try { PlatformAndroid.SaninaVpnService.StopVpn(Android.App.Application.Context, fromUi: true); } catch { }
            }
            ConnectionState = ConnectionState.Error;
            StatusText = "Ошибка подключения";
            await ShowAlert("Ошибка", ex.Message);
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private void Disconnect()
    {
        try
        {
            _cts?.Cancel();
            ConnectionState = ConnectionState.Disconnecting;
            StatusText = "Отключение...";

            XrayProcessManager.Stop();

            if (ConnectionMode == ConnectionMode.Tun || PlatformAndroid.SaninaVpnService.IsRunning)
            {
                try { PlatformAndroid.SaninaVpnService.StopVpn(Android.App.Application.Context, fromUi: true); }
                catch (Exception ex) { Logger.Error($"StopVpn exception: {ex.Message}"); }
            }

            ConnectionState = ConnectionState.Disconnected;
            StatusText = "Не подключено";
            TimerText = "00:00";
            IpText = "";
            DownloadSpeedText = "0 B/s";
            UploadSpeedText = "0 B/s";
            TotalTransferredText = "0 B";
            Logger.Log("Disconnected");
        }
        catch (Exception ex)
        {
            Logger.Error($"Disconnect error: {ex.Message}");
        }
    }

    private async Task DetectIp(CancellationToken token)
    {
        try { await Task.Delay(1800, token); }
        catch (OperationCanceledException) { return; }

        for (int attempt = 0; attempt < 3 && !token.IsCancellationRequested; attempt++)
        {
            try
            {
                using var handler = new HttpClientHandler { Proxy = Config.CreateLocalHttpProxy(), UseProxy = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
                var response = await client.GetStringAsync("https://api.ipify.org?format=json", token);
                var json = JObject.Parse(response);
                string? ip = json["ip"]?.ToString();
                if (!string.IsNullOrEmpty(ip))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested && ConnectionState == ConnectionState.Connected)
                            IpText = ip;
                    });
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch { }

            if (!token.IsCancellationRequested)
            {
                try { await Task.Delay(2500, token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private static (long Rx, long Tx) ReadTrafficBytes()
    {
        int uid = global::Android.OS.Process.MyUid();
        long uidRx = TrafficStats.GetUidRxBytes(uid);
        long uidTx = TrafficStats.GetUidTxBytes(uid);
        if (uidRx >= 0 && uidTx >= 0)
            return (uidRx, uidTx);
        return (Math.Max(0, TrafficStats.TotalRxBytes), Math.Max(0, TrafficStats.TotalTxBytes));
    }

    private async Task MonitorTrafficLoop(CancellationToken token)
    {
        var (lastRx, lastTx) = ReadTrafficBytes();
        long sessionBytes = 0;
        var lastTime = DateTime.UtcNow;

        try
        {
            while (!token.IsCancellationRequested && ConnectionState == ConnectionState.Connected)
            {
                await Task.Delay(1000, token);
                if (token.IsCancellationRequested) break;

                var now = DateTime.UtcNow;
                double elapsed = Math.Max(0.25, (now - lastTime).TotalSeconds);
                lastTime = now;

                var (curRx, curTx) = ReadTrafficBytes();
                long dRx = Math.Max(0, curRx - lastRx);
                long dTx = Math.Max(0, curTx - lastTx);
                lastRx = curRx;
                lastTx = curTx;

                sessionBytes += dRx + dTx;
                double downSpeed = dRx / elapsed;
                double upSpeed = dTx / elapsed;
                var uptime = DateTime.Now - _connectionStartTime;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (token.IsCancellationRequested || ConnectionState != ConnectionState.Connected)
                        return;
                    TimerText = uptime.TotalHours >= 1.0
                        ? $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"
                        : $"{uptime.Minutes:D2}:{uptime.Seconds:D2}";
                    DownloadSpeedText = NetworkUtils.FormatSpeed(downSpeed);
                    UploadSpeedText = NetworkUtils.FormatSpeed(upSpeed);
                    TotalTransferredText = NetworkUtils.FormatBytes(sessionBytes);
                    SpeedUpdated?.Invoke(upSpeed / 1024.0, downSpeed / 1024.0);
                    if (ShowMemoryUsage)
                        UpdateMemoryUsageText();
                    PlatformAndroid.SaninaVpnService.UpdateSpeedNotification(DownloadSpeedText, UploadSpeedText);
                });
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task HealthCheckLoop(CancellationToken token)
    {
        int failures = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Config.HealthCheckInterval), token);
                if (token.IsCancellationRequested || ConnectionState != ConnectionState.Connected)
                    break;

                if (!XrayProcessManager.IsRunning)
                {
                    Logger.Error("Xray process died unexpectedly");
                    if (!Config.AutoReconnectEnabled)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            Disconnect();
                            StatusText = "Туннель остановлен";
                        });
                        break;
                    }
                    failures = Config.ReconnectMaxAttempts;
                }
                else
                {
                    try
                    {
                        using var handler = new HttpClientHandler
                        {
                            Proxy = Config.CreateLocalHttpProxy(),
                            UseProxy = true
                        };
                        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                        var response = await client.GetAsync("https://www.gstatic.com/generate_204", token);
                        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
                            failures = 0;
                        else
                            failures++;
                    }
                    catch (OperationCanceledException) { break; }
                    catch { failures++; }
                }

                if (Config.AutoReconnectEnabled && failures >= Config.ReconnectMaxAttempts && !_isReconnecting && !_isConnecting && !_isTransitioning)
                {
                    _isReconnecting = true;
                    _isTransitioning = true;
                    Logger.Log("Health check failed - attempting reconnect");
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            Disconnect();
                            StatusText = "Переподключение...";
                            await Task.Delay(1500);
                            await Connect();
                        }
                        finally
                        {
                            _isReconnecting = false;
                            _isTransitioning = false;
                        }
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logger.Error($"HealthCheckLoop error: {ex.Message}"); }
    }

    private void RestoreSelectedServerAfterUpdate(ServerInfo? prevSelected, string? preferredSubUrl = null)
    {
        ServerInfo? match = null;
        if (prevSelected != null)
        {
            if (!string.IsNullOrEmpty(prevSelected.Uri))
                match = Servers.FirstOrDefault(s => s.Uri == prevSelected.Uri);
            if (match == null && !string.IsNullOrEmpty(prevSelected.DedupeKey))
                match = Servers.FirstOrDefault(s => s.DedupeKey == prevSelected.DedupeKey);
            if (match == null && !string.IsNullOrEmpty(prevSelected.SubscriptionUrl))
                match = Servers.FirstOrDefault(s =>
                    s.SubscriptionUrl == prevSelected.SubscriptionUrl &&
                    string.Equals(s.Name, prevSelected.Name, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                match = Servers.FirstOrDefault(s =>
                    string.Equals(s.Name, prevSelected.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Host, prevSelected.Host, StringComparison.OrdinalIgnoreCase) &&
                    s.Port == prevSelected.Port);
            if (match == null)
            {
                string subUrl = !string.IsNullOrEmpty(preferredSubUrl) ? preferredSubUrl : prevSelected.SubscriptionUrl;
                if (!string.IsNullOrEmpty(subUrl))
                    match = Servers.FirstOrDefault(s => s.SubscriptionUrl == subUrl);
            }
            if (match == null)
                match = Servers.FirstOrDefault();

            if (match != null && match.Ping <= 0 && prevSelected.Ping > 0)
                match.Ping = prevSelected.Ping;
        }

        foreach (var s in Servers)
            s.IsSelected = false;

        _selectedServer = match;
        if (_selectedServer != null)
        {
            _selectedServer.IsSelected = true;
            Settings["selected_server_uri"] = _selectedServer.Uri;
        }

        OnPropertyChanged(nameof(SelectedServer));
        OnPropertyChanged(nameof(HasSelectedServer));
        OnPropertyChanged(nameof(ServerLabel));
        OnPropertyChanged(nameof(SelectedServerDetails));
        OnPropertyChanged(nameof(SelectedServerFlag));
        OnPropertyChanged(nameof(SelectedServerCountryBadge));
        OnPropertyChanged(nameof(SelectedServerPing));
        OnPropertyChanged(nameof(SelectedServerPingText));
        OnPropertyChanged(nameof(CheckLatencyPing));
        OnPropertyChanged(nameof(CheckLatencyText));
        OnPropertyChanged(nameof(HasCheckPingDot));
        OnPropertyChanged(nameof(CheckStatusGlyph));
        OnPropertyChanged(nameof(ActiveSubscriptionGroup));
        OnPropertyChanged(nameof(HasActiveGroup));
        SyncActiveSubscriptionMetadata();
    }

    private async Task UpdateSingleSubscriptionAsync(string url, SubscriptionGroup? group = null)
    {
        if (group?.IsUpdating == true) return;
        if (group != null) group.IsUpdating = true;

        await _subUpdateSem.WaitAsync();
        if (group == null) IsBusy = true;
        try
        {
            var (servers, meta) = await SubscriptionManager.LoadSubscriptionWithMetadata(url);
            if (servers.Count == 0)
            {
                await ShowAlert("Пусто", "Подписка не вернула ни одного поддерживаемого сервера.");
                return;
            }

            if (GetBool("no_duplicates", true))
                servers = servers.GroupBy(s => s.DedupeKey).Select(g => g.First()).ToList();

            foreach (var s in servers)
                s.SubscriptionUrl = url;
            SubscriptionMetadataMap[url] = meta;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var prevSelected = SelectedServer;
                var old = Servers.Where(s => s.SubscriptionUrl == url).ToList();
                foreach (var s in old) Servers.Remove(s);
                foreach (var s in servers) Servers.Add(s);

                ApplyDefaultSort();
                RefreshFilteredServers();
                RebuildGroups();
                if (prevSelected != null)
                    RestoreSelectedServerAfterUpdate(prevSelected, url);
                SaveData();
            });

            await NetworkUtils.PingServersParallelAsync(servers);
            _ = Task.Run(() => GeoIPService.ResolveServerCountries(servers, _appDir));
            SaveData();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to update group {url}: {ex.Message}");
            await ShowAlert("Ошибка обновления", ex.Message);
        }
        finally
        {
            if (group != null) group.IsUpdating = false;
            else IsBusy = false;
            _subUpdateSem.Release();
        }
    }

    public async Task UpdateSubscriptions(bool silent = false)
    {
        if (IsBusy) return;
        if (Subscriptions.Count == 0)
        {
            if (!silent)
                await ShowAlert("Нет подписок", "Сначала добавьте хотя бы одну подписку.");
            return;
        }

        IsBusy = true;
        await _subUpdateSem.WaitAsync();
        try
        {
            var manualServers = Servers.Where(s =>
                string.IsNullOrEmpty(s.SubscriptionUrl) || !Subscriptions.Contains(s.SubscriptionUrl)).ToList();

            var updatedServers = new List<ServerInfo>();
            var errors = new List<string>();
            int newlyLoadedCount = 0;

            foreach (var url in Subscriptions.ToList())
            {
                try
                {
                    var (servers, meta) = await SubscriptionManager.LoadSubscriptionWithMetadata(url);
                    if (servers.Count == 0)
                    {
                        errors.Add("Подписка не вернула ни одного поддерживаемого сервера.");
                        updatedServers.AddRange(Servers.Where(s => s.SubscriptionUrl == url));
                        continue;
                    }
                    foreach (var s in servers) s.SubscriptionUrl = url;
                    SubscriptionMetadataMap[url] = meta;
                    updatedServers.AddRange(servers);
                    newlyLoadedCount += servers.Count;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Subscription load error ({url}): {ex.Message}");
                    errors.Add(ex.Message);
                    updatedServers.AddRange(Servers.Where(s => s.SubscriptionUrl == url));
                }
            }

            if (GetBool("no_duplicates", true))
                updatedServers = updatedServers.GroupBy(s => s.DedupeKey).Select(g => g.First()).ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var prevSelected = SelectedServer;
                Servers.Clear();
                foreach (var s in updatedServers) Servers.Add(s);
                foreach (var s in manualServers) Servers.Add(s);

                ApplyDefaultSort();
                RefreshFilteredServers();
                RebuildGroups();
                if (prevSelected != null)
                    RestoreSelectedServerAfterUpdate(prevSelected);
                SaveData();
            });

            if (updatedServers.Count > 0)
            {
                _ = Task.Run(() => GeoIPService.ResolveServerCountries(updatedServers, _appDir));
                if (GetBool("ping_on_update", true))
                    await NetworkUtils.PingServersParallelAsync(updatedServers);
                SaveData();
            }

            if (!silent && GetBool("update_notify", true) && newlyLoadedCount > 0)
                await ShowAlert("Обновлено", $"Загружено серверов: {newlyLoadedCount}");

            if (!silent && errors.Count > 0 && newlyLoadedCount == 0)
                await ShowAlert("Ошибка обновления", string.Join("\n", errors.Distinct()));
        }
        finally
        {
            _subUpdateSem.Release();
            IsBusy = false;
        }
    }

    private void ApplyDefaultSort()
    {
        string sort = GetString("default_sort", "none");
        if (sort == "ping")
        {
            var ordered = Servers.OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping).ToList();
            Servers.Clear();
            foreach (var s in ordered) Servers.Add(s);
        }
        else if (sort == "name")
        {
            var ordered = Servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Servers.Clear();
            foreach (var s in ordered) Servers.Add(s);
        }
    }

    public async Task AddSubscription(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        url = url.Trim();
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != System.Uri.UriSchemeHttp && parsedUri.Scheme != System.Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsedUri.Host))
        {
            await ShowAlert("Ошибка", "Неверный формат URL. Ссылка должна начинаться с http:// или https://");
            return;
        }

        if (Subscriptions.Contains(url))
        {
            await UpdateSingleSubscriptionAsync(url);
            return;
        }

        Subscriptions.Add(url);
        SaveData();
        await UpdateSingleSubscriptionAsync(url);
    }

    public void DeleteSubscription(int index)
    {
        if (index < 0 || index >= Subscriptions.Count) return;
        var url = Subscriptions[index];
        Subscriptions.RemoveAt(index);
        SubscriptionMetadataMap.Remove(url);

        var toRemove = Servers.Where(s => s.SubscriptionUrl == url).ToList();
        foreach (var s in toRemove)
        {
            if (SelectedServer == s) SelectedServer = null;
            Servers.Remove(s);
        }
        RefreshFilteredServers();
        RebuildGroups();
        SaveData();
    }

    public async Task AddServerUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        uri = uri.Trim();
        if (SubscriptionManager.ValidateServerUri(uri))
        {
            var server = ServerParser.ParseServer(uri);
            Servers.Add(server);
            SelectedServer ??= server;
            RefreshFilteredServers();
            RebuildGroups();
            SaveData();
            _ = NetworkUtils.PingServersParallelAsync(new[] { server });
            _ = Task.Run(() => GeoIPService.ResolveServerCountries(new List<ServerInfo> { server }, _appDir));
        }
        else
        {
            await ShowAlert("Ошибка", "Неподдерживаемый формат ссылки сервера.");
        }
    }

    public async Task ImportMassTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        IsBusy = true;
        try
        {
            var added = await SubscriptionManager.ParseMassLinksAsync(text);
            if (added.Count == 0)
            {
                await ShowAlert("Импорт", "Не найдено поддерживаемых ссылок или подписок.");
                return;
            }

            foreach (var s in added)
            {
                if (!string.IsNullOrEmpty(s.SubscriptionUrl) && !Subscriptions.Contains(s.SubscriptionUrl))
                    Subscriptions.Add(s.SubscriptionUrl);
                Servers.Add(s);
            }
            SelectedServer ??= added[0];
            RefreshFilteredServers();
            RebuildGroups();
            SaveData();

            await NetworkUtils.PingServersParallelAsync(added);
            _ = Task.Run(() => GeoIPService.ResolveServerCountries(added, _appDir));
            SaveData();
        }
        finally { IsBusy = false; }
    }

    public void DeleteServer(ServerInfo server)
    {
        if (server == null) return;
        if (SelectedServer == server) SelectedServer = null;
        Servers.Remove(server);
        RefreshFilteredServers();
        RebuildGroups();
        SaveData();
    }

    private static void CopyToClipboard(string label, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var ctx = global::Android.App.Application.Context;
            var cm = (global::Android.Content.ClipboardManager?)
                ctx.GetSystemService(global::Android.Content.Context.ClipboardService);
            var clip = global::Android.Content.ClipData.NewPlainText(label, text);
            if (cm != null) cm.PrimaryClip = clip;
        }
        catch { }
    }

    public void ResetSettings()
    {
        if (IsConnected) Disconnect();
        Settings = new Dictionary<string, object>
        {
            ["auto_update"] = true,
            ["dark_mode"] = true,
            ["app_theme"] = "dark",
            ["game_mode"] = false,
            ["auto_select_best"] = true,
            ["auto_ping"] = true,
            ["use_tun_mode"] = true,
            ["mux_enabled"] = true,
            ["fragmentation"] = false,
            ["preferred_ip"] = "auto",
            ["http_enabled"] = true,
            ["socks_enabled"] = true,
            ["http_port"] = 10809,
            ["socks_port"] = 10808,
            ["ping_type"] = "tcp",
            ["ping_display"] = "time",
            ["ping_test_url"] = "https://www.gstatic.com/generate_204",
            ["ping_timeout"] = 3,
            ["update_interval"] = 6,
            ["update_notify"] = true,
            ["request_timeout"] = 15,
            ["update_on_open"] = true,
            ["ping_on_open"] = true,
            ["connect_on_open"] = false,
            ["connect_on_boot"] = false,
            ["no_duplicates"] = true,
            ["send_hwid"] = true,
            ["custom_user_agent"] = "v2rayNG/1.9.34",
            ["default_sort"] = "none",
            ["selected_server_uri"] = SelectedServer?.Uri ?? "",
            ["auto_reconnect"] = true,
            ["bypass_lan"] = true,
            ["bypass_ru"] = true,
            ["allow_lan"] = false,
            ["sniffing_route_only"] = true,
            ["block_udp"] = false,
            ["proxy_auth_enabled"] = false,
            ["http_auth_hotspot"] = true,
            ["custom_dns"] = "1.1.1.1",
            ["tun_mtu"] = 1500,
            ["memory_limit_mb"] = 100,
            ["memory_unlimited"] = false,
            ["keep_wakelock"] = true,
            ["notif_show_speed"] = false,
            ["notif_show_server"] = true,
            ["disconnect_on_lock"] = false,
            ["show_memory_usage"] = false,
            ["ping_on_update"] = true,
            ["fragment_packets"] = "tlshello",
            ["fragment_length"] = "100-200",
            ["fragment_interval"] = "10-20",
            ["noises_enabled"] = false,
            ["noise_type"] = "rand",
            ["noise_packet"] = "50-100",
            ["noise_delay"] = "10-15",
            ["mux_concurrency"] = 8,
            ["mux_xudp_concurrency"] = 16,
            ["mux_xudp_quic"] = "reject",
            ["per_app_proxy_enabled"] = false,
            ["per_app_proxy_mode"] = "bypass",
            ["per_app_packages"] = "",
            ["geo_auto_update"] = true,
            ["geo_update_interval_days"] = 7,
            ["geo_last_updated_unix"] = GetLong("geo_last_updated_unix", 0)
        };
        Theme = ThemeMode.Dark;
        ConnectionMode = ConnectionMode.Tun;
        ApplyRuntimeSettings();
        OnPropertyChanged(nameof(AutoUpdate));
        OnPropertyChanged(nameof(AutoPing));
        OnPropertyChanged(nameof(AutoSelectBest));
        OnPropertyChanged(nameof(MuxEnabled));
        OnPropertyChanged(nameof(FragmentationEnabled));
        OnPropertyChanged(nameof(DisconnectOnLock));
        OnPropertyChanged(nameof(ShowMemoryUsage));
        OnPropertyChanged(nameof(GeoDataSummary));
        SaveData();
    }

    public Task ShowAlert(string title, string message)
        => HivoxDialog.ShowAlertAsync(title, message, "OK");

    public Task<bool> ShowConfirm(string title, string message)
        => HivoxDialog.ShowConfirmAsync(title, message, "Да", "Отмена");

    public void LoadData()
    {
        var (subscriptions, servers, history, settings, subMeta) = DataManager.LoadAppData(_appDir);
        Settings = settings;

        ConnectionHistory.Clear();
        foreach (var h in history) ConnectionHistory.Add(h);

        Subscriptions.Clear();
        foreach (var sub in subscriptions) Subscriptions.Add(sub);

        SubscriptionMetadataMap.Clear();
        foreach (var kv in subMeta) SubscriptionMetadataMap[kv.Key] = kv.Value;

        string themeStr = GetString("app_theme", "");
        _theme = themeStr switch
        {
            "black" => ThemeMode.Black,
            "light" => ThemeMode.Light,
            "materialyou" => ThemeMode.MaterialYou,
            "dark" => ThemeMode.Dark,
            _ => GetBool("dark_mode", true) ? ThemeMode.Dark : ThemeMode.Light
        };
        ThemeManager.ApplyThemeResources(_theme);
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeDisplayName));

        _connectionMode = GetBool("use_tun_mode", true) ? ConnectionMode.Tun : ConnectionMode.Proxy;
        OnPropertyChanged(nameof(ConnectionMode));
        OnPropertyChanged(nameof(IsTunMode));
        OnPropertyChanged(nameof(ConnectionModeName));
        OnPropertyChanged(nameof(ModeShortLabel));
        OnPropertyChanged(nameof(ModeSubtitle));

        Servers.Clear();
        foreach (var s in servers) Servers.Add(s);

        ApplyRuntimeSettings();

        RefreshFilteredServers();
        RebuildGroups();

        string savedUri = GetString("selected_server_uri", "");
        if (!string.IsNullOrEmpty(savedUri))
            SelectedServer = Servers.FirstOrDefault(s => s.Uri == savedUri);

        if (SelectedServer == null && GetBool("auto_select_best", true) && Servers.Count > 0)
            SelectBestServer();
    }

    public void OnScreenLockedDisconnect()
    {
        if (DisconnectOnLock && IsConnected)
        {
            Logger.Log("Screen locked -> disconnecting tunnel (disconnect_on_lock=true)");
            Disconnect();
        }
    }

    public string ExportBackupJson()
    {
        SaveData();
        return DataManager.BuildBackupJson(
            Subscriptions.ToList(),
            Servers.ToList(),
            ConnectionHistory.ToList(),
            Settings,
            SubscriptionMetadataMap);
    }

    public Task<bool> ImportBackupJsonAsync(string json)
    {
        try
        {
            var parsed = JObject.Parse(json);
            if (parsed["servers"] == null && parsed["subscriptions"] == null && parsed["settings"] == null)
                return Task.FromResult(false);

            if (IsConnected) Disconnect();

            var subs = (parsed["subscriptions"] as JArray)?.ToObject<List<string>>() ?? new List<string>();
            var srvs = new List<ServerInfo>();
            if (parsed["servers"] is JArray srvArr)
            {
                foreach (var item in srvArr.OfType<JObject>())
                {
                    string proto = item["protocol"]?.ToString() ?? "Unknown";
                    string tr = item["transport"]?.ToString() ?? "";
                    string sec = item["security"]?.ToString() ?? "";
                    string details = item["details"]?.ToString() ?? $"{proto} / {(string.IsNullOrEmpty(tr) ? "TCP" : tr)} / {(string.IsNullOrEmpty(sec) ? "none" : sec)}";
                    srvs.Add(new ServerInfo
                    {
                        Uri = item["uri"]?.ToString() ?? "",
                        Name = item["name"]?.ToString() ?? "Unknown",
                        Protocol = proto,
                        Transport = tr,
                        Security = sec,
                        Details = details,
                        Host = item["host"]?.ToString() ?? "",
                        Ping = item["ping"]?.ToObject<int>() ?? -1,
                        CountryCode = item["country_code"]?.ToString() ?? "",
                        CountryName = item["country_name"]?.ToString() ?? "Unknown",
                        Flag = item["flag"]?.ToString() ?? "\U0001F310",
                        Port = item["port"]?.ToObject<int>() ?? 443,
                        SubscriptionUrl = item["subscription_url"]?.ToString() ?? "",
                        RawJsonConfig = item["raw_json_config"]?.ToString() ?? ""
                    });
                }
            }

            var hist = (parsed["history"] as JArray)?.ToObject<List<Dictionary<string, string>>>() ?? new List<Dictionary<string, string>>();
            var st = new Dictionary<string, object>();
            if (parsed["settings"] is JObject stObj)
            {
                foreach (var kv in stObj)
                {
                    if (kv.Value is JValue jVal)
                    {
                        if (jVal.Type == JTokenType.Boolean) st[kv.Key] = (bool)jVal;
                        else if (jVal.Type == JTokenType.Integer)
                        {
                            long lv = (long)jVal;
                            st[kv.Key] = lv is >= int.MinValue and <= int.MaxValue ? (int)lv : lv;
                        }
                        else st[kv.Key] = jVal.ToString();
                    }
                }
            }

            var meta = new Dictionary<string, SubscriptionMetadata>();
            if (parsed["subscription_meta"] is JObject metaObj)
            {
                foreach (var kv in metaObj)
                {
                    if (kv.Value is not JObject m) continue;
                    meta[kv.Key] = new SubscriptionMetadata
                    {
                        ProfileTitle = m["profile_title"]?.ToString() ?? m["ProfileTitle"]?.ToString() ?? "",
                        UploadBytes = m["upload_bytes"]?.ToObject<long>() ?? 0,
                        DownloadBytes = m["download_bytes"]?.ToObject<long>() ?? 0,
                        TotalBytes = m["total_bytes"]?.ToObject<long>() ?? 0,
                        ExpireUnix = m["expire_unix"]?.ToObject<long>() ?? 0,
                        UpdateIntervalHours = m["update_interval_hours"]?.ToObject<int>() ?? 0,
                        LastUpdatedUnix = m["last_updated_unix"]?.ToObject<long>() ?? 0,
                        SupportUrl = m["support_url"]?.ToString() ?? "",
                        WebPageUrl = m["web_page_url"]?.ToString() ?? "",
                        CustomName = m["custom_name"]?.ToString() ?? "",
                        ShowOnHome = m["show_on_home"]?.ToObject<bool>() ?? true,
                        AlwaysExpanded = m["always_expanded"]?.ToObject<bool>() ?? false,
                        UseCustomInterval = m["use_custom_interval"]?.ToObject<bool>() ?? false,
                        CustomIntervalMinutes = m["custom_interval_minutes"]?.ToObject<int>() ?? 120,
                        CustomDns = m["custom_dns"]?.ToString() ?? ""
                    };
                }
            }

            DataManager.SaveAppData(_appDir, subs, srvs, hist, st, meta);
            LoadData();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Backup import failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public void ApplyProviderSettings(
        string url,
        SubscriptionMetadata updated,
        string pingDisplayMode,
        string pingProtocol,
        string pingTestUrl,
        string serverSort)
    {
        if (!string.IsNullOrEmpty(url))
            SubscriptionMetadataMap[url] = updated;

        Settings["ping_display"] = pingDisplayMode;
        Settings["ping_type"] = pingProtocol;
        Settings["ping_test_url"] = pingTestUrl;
        Settings["default_sort"] = serverSort;
        if (!string.IsNullOrWhiteSpace(updated.CustomDns))
            Settings["custom_dns"] = updated.CustomDns.Trim();

        ApplyRuntimeSettings();
        foreach (var s in Servers)
            s.RefreshPingDisplay();

        ApplyDefaultSort();
        RefreshFilteredServers();
        RebuildGroups();
        SaveData();
    }

    public void Cleanup()
    {
        _cts?.Cancel();
        _pingCts?.Cancel();
        _netDebounceCts?.Cancel();
        Disconnect();
    }
}
