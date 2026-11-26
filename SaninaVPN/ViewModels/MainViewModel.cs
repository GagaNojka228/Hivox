using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Hivox.Helpers;
using Hivox.Models;
using Hivox.Services;

namespace Hivox.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
    }

    public class MainViewModel : BaseViewModel
    {
        private string _appDir;
        public string AppDir => _appDir;
        private SystemTrayHelper _trayHelper;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _connCts;
        private Process _coreProcess;
        private Process _tunBridgeProcess;

        public ObservableCollection<ServerInfo> Servers { get; set; } = new();
        public ObservableCollection<Dictionary<string, string>> ConnectionHistory { get; set; } = new();
        public List<string> Subscriptions { get; set; } = new();

        private Models.AppTheme _theme = Models.AppTheme.Dark;
        public Models.AppTheme Theme
        {
            get => _theme;
            set { _theme = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDarkTheme)); OnPropertyChanged(nameof(ThemeDisplayName)); ApplyTheme(); }
        }

        public bool IsDarkTheme => _theme != Models.AppTheme.Light;

        public string ThemeDisplayName => _theme switch
        {
            Models.AppTheme.Black => "Чёрная",
            Models.AppTheme.Light => "Светлая",
            _ => "Тёмная"
        };

        private ConnectionState _connectionState = ConnectionState.Disconnected;
        public ConnectionState ConnectionState
        {
            get => _connectionState;
            set
            {
                _connectionState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsConnecting));
                OnPropertyChanged(nameof(IsDisconnected));
                OnPropertyChanged(nameof(ConnectButtonIcon));
                OnPropertyChanged(nameof(ConnectActionText));
                OnConnectionStateChanged();
            }
        }

        public bool IsConnected => _connectionState == Models.ConnectionState.Connected;
        public bool IsConnecting => _connectionState == Models.ConnectionState.Connecting;
        public bool IsDisconnected => _connectionState == Models.ConnectionState.Disconnected;
        public string ConnectButtonIcon => IsConnected ? "⏸" : "▶";
        public string ConnectActionText => IsConnected ? "ОТКЛЮЧИТЬ" : IsConnecting ? "ПОДКЛЮЧЕНИЕ" : "ПОДКЛЮЧИТЬ";

        private ConnectionMode _connectionMode = ConnectionMode.Proxy;
        public ConnectionMode ConnectionMode
        {
            get => _connectionMode;
            set { _connectionMode = value; OnPropertyChanged(); }
        }

        private CoreType _coreType = CoreType.Xray;
        public CoreType CoreType
        {
            get => _coreType;
            set { _coreType = value; OnPropertyChanged(); }
        }

        private bool _gameMode;
        public bool GameMode
        {
            get => _gameMode;
            set { _gameMode = value; OnPropertyChanged(); }
        }

        private int _selectedServerIndex = -1;
        public int SelectedServerIndex
        {
            get => _selectedServerIndex;
            set
            {
                _selectedServerIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedServer));
                if (SelectedServer != null)
                {
                    var country = !string.IsNullOrWhiteSpace(SelectedServer.CountryName) && SelectedServer.CountryName != "Unknown"
                        ? SelectedServer.CountryName
                        : "";
                    ServerLabel = string.IsNullOrEmpty(country)
                        ? SelectedServer.Name
                        : $"{SelectedServer.Name} • {country}";
                }
                else
                {
                    ServerLabel = "Выберите сервер";
                }
            }
        }

        public string AppVersionText
        {
            get
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? $"v{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}" : "v2.1.1";
            }
        }

        public string AppVersionLabel => $"Версия {AppVersionText.TrimStart('v')}";

        private void RestoreSelectedServer(ServerInfo previous)
        {
            if (previous == null || Servers.Count == 0) return;

            int idx = -1;
            if (!string.IsNullOrEmpty(previous.DedupeKey))
            {
                for (int i = 0; i < Servers.Count; i++)
                {
                    if (Servers[i].DedupeKey == previous.DedupeKey) { idx = i; break; }
                }
            }
            if (idx < 0 && !string.IsNullOrEmpty(previous.Host))
            {
                for (int i = 0; i < Servers.Count; i++)
                {
                    if (string.Equals(Servers[i].Host, previous.Host, StringComparison.OrdinalIgnoreCase) &&
                        Servers[i].Port == previous.Port &&
                        string.Equals(Servers[i].Name, previous.Name, StringComparison.Ordinal))
                    {
                        idx = i;
                        break;
                    }
                }
            }
            if (idx < 0 && !string.IsNullOrEmpty(previous.Name))
            {
                for (int i = 0; i < Servers.Count; i++)
                {
                    if (string.Equals(Servers[i].Name, previous.Name, StringComparison.Ordinal))
                    {
                        idx = i;
                        break;
                    }
                }
            }

            if (idx >= 0)
            {
                SelectedServerIndex = idx;
                OnPropertyChanged(nameof(SelectedServer));
            }
        }

        public ServerInfo SelectedServer
        {
            get => SelectedServerIndex >= 0 && SelectedServerIndex < Servers.Count ? Servers[SelectedServerIndex] : null;
            set => SelectedServerIndex = value != null ? Servers.IndexOf(value) : -1;
        }

        private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
        public Dictionary<string, SubscriptionMetadata> SubscriptionMetas { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public (string Url, SubscriptionMetadata Meta) GetGroupMetadata(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return ("", null);

            var srv = Servers.FirstOrDefault(s => string.Equals(s.GroupTitle, groupName, StringComparison.Ordinal));
            string url = srv?.SubscriptionUrl ?? "";
            if (!string.IsNullOrWhiteSpace(url) && SubscriptionMetas.TryGetValue(url, out var meta))
                return (url, meta);

            foreach (var subUrl in Subscriptions)
            {
                if (SubscriptionMetas.TryGetValue(subUrl, out var m))
                {
                    string title = !string.IsNullOrWhiteSpace(m.CustomName)
                        ? m.CustomName
                        : !string.IsNullOrWhiteSpace(m.ProfileTitle)
                            ? m.ProfileTitle
                            : ServerInfo.GetSubscriptionDisplayName(subUrl);
                    if (string.Equals(title, groupName, StringComparison.Ordinal))
                        return (subUrl, m);
                }
                else if (string.Equals(ServerInfo.GetSubscriptionDisplayName(subUrl), groupName, StringComparison.Ordinal))
                {
                    return (subUrl, null);
                }
            }

            return (url, null);
        }

        private static void PreserveMetaOverrides(SubscriptionMetadata prev, SubscriptionMetadata next)
        {
            if (prev == null || next == null) return;
            next.CustomName = prev.CustomName;
            next.NeverCollapse = prev.NeverCollapse;
            next.CustomUpdateIntervalMinutes = prev.CustomUpdateIntervalMinutes;
            next.CustomDns = prev.CustomDns;
            next.CustomSort = prev.CustomSort;
        }

        private static string ResolveSubDisplayName(string url, SubscriptionMetadata meta)
        {
            if (!string.IsNullOrWhiteSpace(meta?.CustomName))
                return meta.CustomName.Trim();
            if (!string.IsNullOrWhiteSpace(meta?.ProfileTitle))
                return meta.ProfileTitle.Trim();
            return ServerInfo.GetSubscriptionDisplayName(url);
        }

        public void DeleteSubscriptionGroup(string groupName, string url)
        {
            var prev = SelectedServer;
            var toRemove = Servers.Where(s =>
                !string.IsNullOrWhiteSpace(url)
                    ? string.Equals(s.SubscriptionUrl, url, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(s.GroupTitle, groupName, StringComparison.Ordinal)).ToList();

            foreach (var s in toRemove)
                Servers.Remove(s);

            if (!string.IsNullOrWhiteSpace(url))
            {
                Subscriptions.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase));
                SubscriptionMetas.Remove(url);
            }

            if (prev != null && Servers.Contains(prev))
                SelectedServer = prev;
            else
                SelectedServerIndex = Servers.Count > 0 ? 0 : -1;

            StatusText = $"Удалена подписка «{groupName}»";
            SaveData();
        }

        public void ApplySubscriptionEdits(string oldGroupName, string url, string customName, bool neverCollapse,
            int customIntervalMin, string customDns, string customSort)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (!SubscriptionMetas.TryGetValue(url, out var meta) || meta == null)
                {
                    meta = new SubscriptionMetadata { ProfileTitle = oldGroupName };
                    SubscriptionMetas[url] = meta;
                }
                meta.CustomName = (customName ?? "").Trim();
                meta.NeverCollapse = neverCollapse;
                meta.CustomUpdateIntervalMinutes = Math.Max(0, customIntervalMin);
                meta.CustomDns = (customDns ?? "").Trim();
                meta.CustomSort = customSort ?? "";

                string newTitle = ResolveSubDisplayName(url, meta);
                foreach (var s in Servers.Where(s => string.Equals(s.SubscriptionUrl, url, StringComparison.OrdinalIgnoreCase)))
                    s.SubscriptionName = newTitle;

                if (neverCollapse)
                    _collapsedGroups.Remove(newTitle);
            }

            if (customSort == "ping" || customSort == "alpha")
            {
                string targetGroup = !string.IsNullOrWhiteSpace(customName) ? customName.Trim() : oldGroupName;
                var prev = SelectedServer;
                var groupItems = Servers.Where(s =>
                    !string.IsNullOrWhiteSpace(url)
                        ? string.Equals(s.SubscriptionUrl, url, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(s.GroupTitle, targetGroup, StringComparison.Ordinal)).ToList();

                if (groupItems.Count > 1)
                {
                    var sorted = customSort == "ping"
                        ? groupItems.OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping).ToList()
                        : groupItems.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

                    int firstIdx = Servers.IndexOf(groupItems[0]);
                    foreach (var item in groupItems)
                        Servers.Remove(item);
                    for (int i = 0; i < sorted.Count; i++)
                        Servers.Insert(Math.Min(firstIdx + i, Servers.Count), sorted[i]);
                    if (prev != null)
                        SelectedServerIndex = Servers.IndexOf(prev);
                }
            }

            Settings["collapsed_groups"] = string.Join("\n", _collapsedGroups);
            SaveData();
        }

        public string GetGroupShareText(string groupName, string url)
        {
            if (!string.IsNullOrWhiteSpace(url))
                return url;
            var uris = Servers
                .Where(s => string.Equals(s.GroupTitle, groupName, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(s.Uri))
                .Select(s => s.Uri);
            return string.Join("\n", uris);
        }

        public void PingGroupServers(string groupName)
        {
            var targets = Servers.Where(s => string.Equals(s.GroupTitle, groupName, StringComparison.Ordinal)).ToList();
            if (targets.Count == 0) return;

            StatusText = $"Проверка пинга ({groupName})...";
            Task.Run(async () =>
            {
                await NetworkUtils.PingServersParallelAsync(targets);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    int alive = targets.Count(s => s.Ping > 0);
                    StatusText = $"Готово: {alive}/{targets.Count} доступно";
                    SaveData();
                });
            });
        }

        public async Task<(bool Success, string Message)> RefreshSingleSubscriptionAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return (false, "Ссылка подписки не найдена");

            StatusText = "Обновление подписки...";
            try
            {
                bool sendHwid = GetBool("send_hwid", true);
                int timeout = GetInt("request_timeout", 15);
                var (srv, meta) = await SubscriptionManager.LoadSubscriptionWithMetadata(url, sendHwid, timeout);
                if (srv.Count == 0)
                    return (false, "Подписка не содержит серверов");

                if (meta != null)
                {
                    SubscriptionMetas.TryGetValue(url, out var oldMeta);
                    PreserveMetaOverrides(oldMeta, meta);
                    meta.LastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    SubscriptionMetas[url] = meta;
                    ApplySubscriptionMetadata(meta);
                }

                string subName = ResolveSubDisplayName(url, meta ?? (SubscriptionMetas.TryGetValue(url, out var m2) ? m2 : null));
                foreach (var s in srv)
                {
                    s.SubscriptionUrl = url;
                    s.SubscriptionName = subName;
                }

                var prevServer = SelectedServer;
                var others = Servers.Where(s => !string.Equals(s.SubscriptionUrl, url, StringComparison.OrdinalIgnoreCase)).ToList();
                var combined = new List<ServerInfo>(others.Count + srv.Count);
                combined.AddRange(others);
                combined.AddRange(srv);
                if (GetBool("no_duplicates", true))
                    combined = combined.GroupBy(s => s.DedupeKey).Select(g => g.First()).ToList();
                combined = ServerParser.DeduplicateNames(combined);

                Servers.Clear();
                foreach (var s in combined) Servers.Add(s);
                RestoreSelectedServer(prevServer);

                StatusText = $"Обновлено: {srv.Count} серв.";
                SaveData();
                PingGroupServers(subName);
                _ = Task.Run(async () =>
                {
                    if (await GeoIPService.ResolveServerCountries(Servers.ToList(), _appDir))
                        Application.Current.Dispatcher.Invoke(SaveData);
                });
                return (true, $"Обновлено серверов: {srv.Count}");
            }
            catch (Exception ex)
            {
                StatusText = "Ошибка обновления подписки";
                return (false, ex.Message);
            }
        }

        public bool IsGroupCollapsed(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return false;
            var (_, meta) = GetGroupMetadata(groupName);
            if (meta?.NeverCollapse == true) return false;
            return _collapsedGroups.Contains(groupName);
        }

        public void SetGroupCollapsed(string groupName, bool collapsed)
        {
            if (string.IsNullOrEmpty(groupName)) return;
            bool changed = collapsed ? _collapsedGroups.Add(groupName) : _collapsedGroups.Remove(groupName);
            if (changed)
            {
                Settings["collapsed_groups"] = string.Join("\n", _collapsedGroups);
                SaveData();
            }
        }

        public void SetAllGroupsCollapsed(bool collapsed)
        {
            _collapsedGroups.Clear();
            if (collapsed)
            {
                foreach (var g in Servers.Select(s => s.GroupTitle).Distinct())
                    _collapsedGroups.Add(g);
            }
            Settings["collapsed_groups"] = string.Join("\n", _collapsedGroups);
            SaveData();
        }

        private string _statusText = "Не подключено";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _serverLabel = "Выберите сервер";
        public string ServerLabel
        {
            get => _serverLabel;
            set { _serverLabel = value; OnPropertyChanged(); }
        }

        private string _timerText = "";
        public string TimerText
        {
            get => _timerText;
            set { _timerText = value; OnPropertyChanged(); }
        }

        private string _ipText = "";
        public string IpText
        {
            get => _ipText;
            set { _ipText = value; OnPropertyChanged(); }
        }

        private string _downloadSpeedText = "0 B/s";
        public string DownloadSpeedText
        {
            get => _downloadSpeedText;
            set { _downloadSpeedText = value; OnPropertyChanged(); }
        }

        private string _uploadSpeedText = "0 B/s";
        public string UploadSpeedText
        {
            get => _uploadSpeedText;
            set { _uploadSpeedText = value; OnPropertyChanged(); }
        }

        private string _totalTransferredText = "0 B";
        public string TotalTransferredText
        {
            get => _totalTransferredText;
            set { _totalTransferredText = value; OnPropertyChanged(); }
        }

        private string _subTitle = "";
        public string SubTitle
        {
            get => _subTitle;
            set { _subTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSubscriptionCard)); }
        }

        private string _subTrafficText = "";
        public string SubTrafficText
        {
            get => _subTrafficText;
            set { _subTrafficText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSubscriptionCard)); }
        }

        private string _subExpireText = "";
        public string SubExpireText
        {
            get => _subExpireText;
            set { _subExpireText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSubscriptionCard)); }
        }

        private string _subAnnounceText = "";
        public string SubAnnounceText
        {
            get => _subAnnounceText;
            set { _subAnnounceText = value; OnPropertyChanged(); }
        }

        private double _subTrafficRatio;
        public double SubTrafficRatio
        {
            get => _subTrafficRatio;
            set { _subTrafficRatio = value; OnPropertyChanged(); }
        }

        public bool HasSubscriptionCard =>
            !string.IsNullOrEmpty(SubTitle) || !string.IsNullOrEmpty(SubTrafficText) || !string.IsNullOrEmpty(SubExpireText);

        public event Action<double, double> SpeedUpdated;

        private bool _proxyEnabled;
        private DateTime _connectionStartTime;
        private double _trafficLastTime;
        public Dictionary<string, object> Settings { get; set; } = new()
        {
            ["auto_update"] = true, ["dark_mode"] = true, ["minimize_to_tray"] = true,
            ["auto_start"] = false, ["show_speed"] = true, ["auto_select_best"] = true,
            ["use_tun_mode"] = false, ["use_singbox"] = false, ["mux_enabled"] = true
        };

        public Dictionary<string, string> Colors { get; private set; }

        public ICommand ToggleConnectionCommand { get; }
        public ICommand SelectServerCommand { get; }
        public ICommand PingAllCommand { get; }
        public ICommand ShowSubscriptionsCommand { get; }
        public ICommand ShowMassImportCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ToggleGameModeCommand { get; }
        public ICommand SortByPingCommand { get; }
        public ICommand AutoSelectConnectCommand { get; }
        public ICommand TestServerCommand { get; }
        public ICommand UpdateSubscriptionsCommand { get; }
        public ICommand ConnectToServerCommand { get; }
        public ICommand CopyServerUriCommand { get; }
        public ICommand DeleteServerCommand { get; }
        public ICommand SortByNameCommand { get; }

        public MainViewModel()
        {
            _cts = new CancellationTokenSource();
            _appDir = DataManager.ResolveDataDirectory();
            Colors = ThemeManager.GetColors(Models.AppTheme.Dark);

            ToggleConnectionCommand = new RelayCommand(_ => ToggleConnection());
            SelectServerCommand = new RelayCommand(idx => SelectServer((int)idx));
            PingAllCommand = new RelayCommand(_ => PingAllServers());
            ShowSubscriptionsCommand = new RelayCommand(_ => ShowSubscriptionsDialog());
            ShowMassImportCommand = new RelayCommand(_ => ShowMassImportDialog());
            ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
            ToggleGameModeCommand = new RelayCommand(_ => ToggleGameMode());
            SortByPingCommand = new RelayCommand(_ => SortByPing());
            AutoSelectConnectCommand = new RelayCommand(_ => AutoSelectAndConnect());
            TestServerCommand = new RelayCommand(param => {
                if (param is ServerInfo s) TestServer(s);
                else TestServer(SelectedServer);
            });
            UpdateSubscriptionsCommand = new RelayCommand(_ => UpdateSubscriptions());
            ConnectToServerCommand = new RelayCommand(server => ConnectToServer(server as ServerInfo));
            CopyServerUriCommand = new RelayCommand(server => CopyServerUri(server as ServerInfo));
            DeleteServerCommand = new RelayCommand(server => DeleteServer(server as ServerInfo));
            SortByNameCommand = new RelayCommand(_ => SortByName());

            ProcessManager.LogDir = _appDir;
            ProcessManager.CleanupStderrFiles();
            ProcessManager.KillProcess("xray.exe");
            ProcessManager.KillProcess("sing-box.exe");
            ProxyManager.SetSystemProxy(false, Config.ProxyPort);
            _ = Task.Run(() =>
            {
                string binDir = AppDomain.CurrentDomain.BaseDirectory;
                ProcessManager.ResolveOrExtractCore("xray.exe", binDir);
                ProcessManager.ResolveOrExtractCore("sing-box.exe", binDir);
                ProcessManager.ResolveOrExtractCore("wintun.dll", binDir);
            });

            LoadData();
            StartBackgroundTasks();
        }

        public void SetTrayHelper(SystemTrayHelper helper) => _trayHelper = helper;

        private void ApplyTheme()
        {
            Colors = ThemeManager.GetColors(Theme);
            ThemeManager.ApplyThemeResources(Theme);
            OnPropertyChanged(nameof(Colors));
        }

        private void ApplySubscriptionMetadata(SubscriptionMetadata meta)
        {
            if (meta == null) return;
            if (!string.IsNullOrEmpty(meta.ProfileTitle))
                Settings["sub_title"] = meta.ProfileTitle;
            if (meta.TotalBytes > 0 || meta.UploadBytes > 0 || meta.DownloadBytes > 0)
            {
                Settings["sub_used"] = meta.UploadBytes + meta.DownloadBytes;
                Settings["sub_total"] = meta.TotalBytes;
            }
            if (meta.ExpireUnix > 0)
                Settings["sub_expire"] = meta.ExpireUnix;
            if (!string.IsNullOrEmpty(meta.AnnounceText))
                Settings["sub_announce"] = meta.AnnounceText;
            if (meta.UpdateIntervalHours > 0)
                Settings["update_interval"] = meta.UpdateIntervalHours;

            RefreshSubscriptionBanner();
        }

        public void RefreshSubscriptionBanner()
        {
            SubTitle = GetString("sub_title", "");
            long used = GetLong("sub_used", 0);
            long total = GetLong("sub_total", 0);
            long expire = GetLong("sub_expire", 0);
            SubAnnounceText = GetString("sub_announce", "");

            var meta = new SubscriptionMetadata
            {
                ProfileTitle = SubTitle,
                DownloadBytes = used,
                TotalBytes = total,
                ExpireUnix = expire
            };
            SubTrafficText = meta.FormattedTraffic;
            SubTrafficRatio = meta.TrafficRatio;
            SubExpireText = meta.FormattedExpire;
        }

        private void LoadData()
        {
            var (subscriptions, servers, history, settings, subMeta) = DataManager.LoadAppData(_appDir);
            Subscriptions = subscriptions;
            Settings = settings;
            SubscriptionMetas = subMeta ?? new Dictionary<string, SubscriptionMetadata>(StringComparer.OrdinalIgnoreCase);
            string themeMode = GetString("theme_mode", "");
            Theme = themeMode switch
            {
                "black" => Models.AppTheme.Black,
                "light" => Models.AppTheme.Light,
                "dark" => Models.AppTheme.Dark,
                _ => GetBool("dark_mode", true) ? Models.AppTheme.Dark : Models.AppTheme.Light
            };
            ConnectionMode = GetBool("use_tun_mode", false) ? ConnectionMode.Tun : ConnectionMode.Proxy;
            CoreType = GetBool("use_singbox", false) ? CoreType.SingBox : CoreType.Xray;

            ApplyRuntimeSettings();
            Config.RequestTimeout = GetInt("request_timeout", 15);
            NetworkUtils.AppDir = AppDomain.CurrentDomain.BaseDirectory;
            NetworkUtils.PingMethod = GetString("ping_type", "proxy");
            NetworkUtils.PingTestUrl = GetString("ping_url", "https://www.gstatic.com/generate_204");
            NetworkUtils.PingTimeoutSec = Math.Clamp(GetInt("ping_timeout", 3), 1, 15);
            Converters.PingToTextConverter.DisplayMode = GetString("ping_display", "time");
            RefreshSubscriptionBanner();

            if (SubscriptionMetas.Count == 0 && Subscriptions.Count > 0 && HasSubscriptionCard)
            {
                SubscriptionMetas[Subscriptions[0]] = new SubscriptionMetadata
                {
                    ProfileTitle = SubTitle,
                    DownloadBytes = GetLong("sub_used", 0),
                    TotalBytes = GetLong("sub_total", 0),
                    ExpireUnix = GetLong("sub_expire", 0),
                    UpdateIntervalHours = GetInt("update_interval", 0)
                };
            }

            _collapsedGroups.Clear();
            foreach (var g in GetString("collapsed_groups", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _collapsedGroups.Add(g);

            Servers.Clear();
            foreach (var s in servers)
            {
                if (string.IsNullOrWhiteSpace(s.SubscriptionUrl) && Subscriptions.Count == 1)
                {
                    s.SubscriptionUrl = Subscriptions[0];
                    s.SubscriptionName = !string.IsNullOrWhiteSpace(SubTitle)
                        ? SubTitle
                        : ServerInfo.GetSubscriptionDisplayName(Subscriptions[0]);
                }
                Servers.Add(s);
            }

            var sortMethod = GetString("sort_method", "none");
            if (sortMethod == "ping")
                SortByPing();
            else if (sortMethod == "alpha")
                SortByName();

            ConnectionHistory.Clear();
            foreach (var h in history) ConnectionHistory.Add(h);

            if ((Servers.Count == 0 && Subscriptions.Count > 0 && GetBool("auto_update", true)) ||
                (Subscriptions.Count > 0 && GetBool("update_on_open", false)))
                Task.Run(() => RecoverServers());
            if (Servers.Any(s => string.IsNullOrWhiteSpace(s.CountryCode) || s.CountryName == "Unknown"))
                _ = Task.Run(async () =>
                {
                    if (await GeoIPService.ResolveServerCountries(Servers.ToList(), _appDir))
                        Application.Current.Dispatcher.Invoke(SaveData);
                });

            if (GetBool("ping_on_open", true) && Servers.Count > 0)
                Task.Run(async () => { await Task.Delay(1500); PingAllServers(); });
            if (GetBool("connect_on_open", false) && Servers.Count > 0)
                Task.Run(async () => { await Task.Delay(3000); Application.Current.Dispatcher.Invoke(AutoSelectAndConnect); });

            if (GetBool("geo_auto_update", true) &&
                (File.Exists(Path.Combine(_appDir, "geoip.dat")) || File.Exists(Path.Combine(_appDir, "geosite.dat"))))
            {
                long lastUnix = GetLong("geo_last_updated_unix", 0);
                int days = Math.Clamp(GetInt("geo_update_interval_days", 7), 1, 90);
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastUnix >= days * 86400L)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(6000);
                            await GeoDataUpdater.UpdateAllAsync(_appDir, IsConnected);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Settings["geo_last_updated_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                SaveData();
                            });
                        }
                        catch { }
                    });
                }
            }
        }

        private void RecoverServers()
        {
            bool sendHwid = GetBool("send_hwid", true);
            int timeout = GetInt("request_timeout", 15);
            SubscriptionMetadata latestMeta = null;
            var loaded = new List<ServerInfo>();
            foreach (var url in Subscriptions)
            {
                try
                {
                    var (servers, meta) = SubscriptionManager.LoadSubscriptionWithMetadata(url, sendHwid, timeout).GetAwaiter().GetResult();
                    if (meta != null)
                    {
                        SubscriptionMetas.TryGetValue(url, out var oldMeta);
                        PreserveMetaOverrides(oldMeta, meta);
                        meta.LastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        SubscriptionMetas[url] = meta;
                        latestMeta = meta;
                    }
                    string subName = ResolveSubDisplayName(url, meta ?? (SubscriptionMetas.TryGetValue(url, out var m2) ? m2 : null));
                    foreach (var s in servers)
                    {
                        s.SubscriptionUrl = url;
                        s.SubscriptionName = subName;
                    }
                    loaded.AddRange(servers);
                }
                catch { }
            }
            if (loaded.Count > 0)
            {
                if (GetBool("no_duplicates", true))
                    loaded = loaded.GroupBy(s => s.DedupeKey).Select(g => g.First()).ToList();
                NetworkUtils.PingServersParallel(loaded);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var prevServer = SelectedServer;
                    Servers.Clear();
                    foreach (var s in loaded) Servers.Add(s);
                    RestoreSelectedServer(prevServer);
                    if (latestMeta != null) ApplySubscriptionMetadata(latestMeta);
                    SaveData();
                    _ = Task.Run(async () =>
                    {
                        if (await GeoIPService.ResolveServerCountries(Servers.ToList(), _appDir))
                            Application.Current.Dispatcher.Invoke(SaveData);
                    });
                });
            }
        }

        public void SaveData()
        {
            DataManager.SaveAppData(_appDir, Subscriptions, Servers.ToList(),
                ConnectionHistory.TakeLastCustom(50).ToList(), Settings, SubscriptionMetas);
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            return Settings.TryGetValue(key, out var val) && val is bool b ? b : defaultValue;
        }

        public string GetString(string key, string defaultValue = "")
        {
            return Settings.TryGetValue(key, out var val) && val is string s ? s : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (!Settings.TryGetValue(key, out var val)) return defaultValue;
            if (val is int i) return i;
            if (val is long l) return (int)l;
            return int.TryParse(val?.ToString(), out int parsed) ? parsed : defaultValue;
        }

        public long GetLong(string key, long defaultValue = 0)
        {
            if (!Settings.TryGetValue(key, out var val)) return defaultValue;
            if (val is long l) return l;
            if (val is int i) return i;
            return long.TryParse(val?.ToString(), out long parsed) ? parsed : defaultValue;
        }

        public void ApplyPortSettings()
        {
            Config.ProxyPort = GetInt("http_port", 10809);
            Config.SocksPort = GetInt("socks_port", 10808);
        }

        public void ApplyRuntimeSettings()
        {
            ApplyPortSettings();
            Config.HttpEnabled = GetBool("http_enabled", true);
            Config.SocksEnabled = GetBool("socks_enabled", true);
            Config.AutoReconnect = GetBool("auto_reconnect", true);
            Config.TunMtu = Math.Clamp(GetInt("tun_mtu", 1420), 1200, 9000);

            string dns = GetString("custom_dns", "1.1.1.1").Trim();
            Config.CustomDns = string.IsNullOrWhiteSpace(dns) ? "1.1.1.1" : dns;

            Config.BypassLan = GetBool("bypass_lan", true);
            Config.BypassRu = GetBool("bypass_ru", true);
            Config.AllowLan = GetBool("allow_lan", false);
            Config.SniffingRouteOnly = GetBool("sniffing_route_only", true);
            Config.BlockUdp = GetBool("block_udp", false);
            Config.DisconnectOnLock = GetBool("disconnect_on_lock", false);

            Config.ProxyAuthEnabled = GetBool("proxy_auth_enabled", false);
            Config.HttpAuthOnHotspot = GetBool("http_auth_hotspot", true);
            Config.ProxyAuthUser = GetString("proxy_auth_user", "");
            Config.ProxyAuthPassword = GetString("proxy_auth_password", "");

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

            Config.ProcessRulesEnabled = GetBool("process_rules_enabled", false);
            string pMode = GetString("process_rules_mode", "bypass");
            Config.ProcessRulesMode = string.Equals(pMode, "only_vpn", StringComparison.OrdinalIgnoreCase) ? "only_vpn" : "bypass";
            Config.ProcessRulesList = GetString("process_rules_list", "")
                .Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Path.GetFileName(p.Trim().Trim('"')))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !p.Contains('.') ? p + ".exe" : p)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void SetWindowsAutoStart(bool enabled)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null) return;
                if (enabled)
                {
                    string exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrWhiteSpace(exe))
                        key.SetValue("Hivox", $"\"{exe}\"");
                }
                else
                {
                    key.DeleteValue("Hivox", throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"AutoStart registry error: {ex.Message}");
            }
        }

        public static bool IsUrlSchemeRegistered()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\hivox");
                return key != null;
            }
            catch { return false; }
        }

        public static bool RegisterUrlScheme(bool register)
        {
            try
            {
                if (!register)
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\hivox", throwOnMissingSubKey: false);
                    return true;
                }

                string exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(exe)) return false;

                using var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\hivox");
                root.SetValue("", "URL:Hivox Protocol");
                root.SetValue("URL Protocol", "");
                using var cmd = root.CreateSubKey(@"shell\open\command");
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"RegisterUrlScheme error: {ex.Message}");
                return false;
            }
        }

        public string ExportBackupJson()
        {
            return DataManager.BuildBackupJson(
                Subscriptions,
                Servers.ToList(),
                ConnectionHistory.TakeLastCustom(Config.MaxHistoryEntries).ToList(),
                Settings,
                SubscriptionMetas);
        }

        public bool ImportBackupJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var doc = JObject.Parse(json);
                var subs = doc["subscriptions"]?.ToObject<List<string>>() ?? new List<string>();
                var hist = doc["history"]?.ToObject<List<Dictionary<string, string>>>() ?? new List<Dictionary<string, string>>();
                var importedServers = new List<ServerInfo>();
                if (doc["servers"] is JArray srvArr)
                {
                    foreach (var item in srvArr.OfType<JObject>())
                    {
                        importedServers.Add(new ServerInfo
                        {
                            Uri = item["uri"]?.ToString() ?? "",
                            Name = item["name"]?.ToString() ?? "Unknown",
                            Protocol = item["protocol"]?.ToString() ?? "Unknown",
                            Transport = item["transport"]?.ToString() ?? "",
                            Security = item["security"]?.ToString() ?? "",
                            Host = item["host"]?.ToString() ?? "",
                            Ping = item["ping"]?.ToObject<int>() ?? -1,
                            CountryCode = item["country_code"]?.ToString() ?? "",
                            CountryName = item["country_name"]?.ToString() ?? "Unknown",
                            Flag = item["flag"]?.ToString() ?? "\U0001F310",
                            Port = item["port"]?.ToObject<int>() ?? 443,
                            Details = item["details"]?.ToString() ?? "",
                            RawJsonConfig = item["raw_json"]?.ToString() ?? item["raw_json_config"]?.ToString() ?? "",
                            SubscriptionUrl = item["subscription_url"]?.ToString() ?? "",
                            SubscriptionName = item["subscription_name"]?.ToString() ?? ""
                        });
                    }
                }

                if (doc["settings"] is JObject stObj)
                {
                    foreach (var kv in stObj)
                    {
                        if (kv.Value is JValue jVal)
                        {
                            if (jVal.Type == JTokenType.Boolean)
                                Settings[kv.Key] = (bool)jVal;
                            else if (jVal.Type == JTokenType.Integer)
                                Settings[kv.Key] = (long)jVal is >= int.MinValue and <= int.MaxValue ? (int)jVal : (long)jVal;
                            else
                                Settings[kv.Key] = jVal.ToString();
                        }
                    }
                }

                Subscriptions = subs;
                Servers.Clear();
                foreach (var s in importedServers) Servers.Add(s);
                ConnectionHistory.Clear();
                foreach (var h in hist) ConnectionHistory.Add(h);

                string themeMode = GetString("theme_mode", "dark");
                Theme = themeMode switch
                {
                    "black" => Models.AppTheme.Black,
                    "light" => Models.AppTheme.Light,
                    _ => Models.AppTheme.Dark
                };
                ConnectionMode = GetBool("use_tun_mode", false) ? ConnectionMode.Tun : ConnectionMode.Proxy;
                CoreType = GetBool("use_singbox", false) ? CoreType.SingBox : CoreType.Xray;

                ApplyRuntimeSettings();
                SaveData();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Backup import error: {ex.Message}");
                return false;
            }
        }

        public async Task<(bool Ok, string Message)> HandleDeepLinkAsync(string rawArg)
        {
            if (string.IsNullOrWhiteSpace(rawArg)) return (false, "Пустая ссылка");
            string input = rawArg.Trim().Trim('"');

            if (input.StartsWith("hivox://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
                {
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    string target = query["url"] ?? query["uri"] ?? "";
                    if (string.IsNullOrWhiteSpace(target))
                        return (false, "В ссылке hivox:// не указан параметр url=");
                    input = target.Trim();
                }
            }

            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var res = await AddAndLoadSubscriptionAsync(input);
                return (res.Success, res.Message);
            }

            var parsed = ServerParser.ParseServer(input);
            if (parsed != null)
            {
                if (!Servers.Any(s => s.DedupeKey == parsed.DedupeKey))
                {
                    Servers.Add(parsed);
                    SelectedServerIndex = Servers.Count - 1;
                    SaveData();
                    return (true, $"Сервер «{parsed.Name}» добавлен.");
                }
                return (true, $"Сервер «{parsed.Name}» уже в списке.");
            }

            return (false, "Неподдерживаемый формат ссылки");
        }

        public async Task RestartActiveConnectionAsync()
        {
            if (!IsConnected) return;
            Disconnect();
            await Task.Delay(500);
            Connect();
        }

        public void ResetSettings()
        {
            Settings.Clear();
            Settings["auto_update"] = true; Settings["dark_mode"] = true; Settings["theme_mode"] = "dark";
            Settings["minimize_to_tray"] = true; Settings["auto_start"] = false;
            Settings["show_speed"] = true; Settings["auto_select_best"] = true;
            Settings["use_tun_mode"] = false; Settings["use_singbox"] = false;
            Settings["mux_enabled"] = true;
            SaveData();
            System.Windows.Application.Current.Shutdown();
        }

        public void SetTheme(Models.AppTheme theme)
        {
            Theme = theme;
            Settings["theme_mode"] = theme switch
            {
                Models.AppTheme.Black => "black",
                Models.AppTheme.Light => "light",
                _ => "dark"
            };
            Settings["dark_mode"] = theme != Models.AppTheme.Light;
            SaveData();
        }

        public void ToggleTheme()
        {
            var next = Theme switch
            {
                Models.AppTheme.Dark => Models.AppTheme.Black,
                Models.AppTheme.Black => Models.AppTheme.Light,
                _ => Models.AppTheme.Dark
            };
            SetTheme(next);
        }

        public void ToggleGameMode()
        {
            GameMode = !GameMode;
        }

        public void SelectServer(int index)
        {
            SelectedServerIndex = index;
        }

        private int FindBestPingServer()
        {
            int bestIdx = -1;
            int bestPing = int.MaxValue;
            for (int i = 0; i < Servers.Count; i++)
            {
                if (Servers[i].Ping > 0 && Servers[i].Ping < bestPing)
                {
                    bestPing = Servers[i].Ping;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        public void PingAllServers()
        {
            StatusText = "Обновление пинга...";
            var list = Servers.ToList();
            Task.Run(async () =>
            {
                await NetworkUtils.PingServersParallelAsync(list);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusText = list.Count > 0 ? "Пинг обновлён" : "Нет серверов";
                });
            });
        }

        public void SortByPing()
        {
            var current = SelectedServer;
            var sorted = Servers.OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping).ToList();
            Servers.Clear();
            foreach (var s in sorted) Servers.Add(s);
            if (current != null) SelectedServerIndex = Servers.IndexOf(current);
        }

        private int _autoConnectRetries;
        public void AutoSelectAndConnect()
        {
            if (_autoConnectRetries >= 3)
            {
                _autoConnectRetries = 0;
                StatusText = "Не удалось найти доступный сервер";
                return;
            }
            if (Servers.Count == 0)
            {
                MessageBox.Show("Нет серверов. Обновите подписки.", "Предупреждение");
                return;
            }
            if (Servers.All(s => s.Ping <= 0))
            {
                _autoConnectRetries++;
                PingAllServers();
                Task.Delay(3000).ContinueWith(_ => Application.Current.Dispatcher.Invoke(AutoSelectAndConnect));
                return;
            }
            _autoConnectRetries = 0;
            int best = FindBestPingServer();
            if (best < 0)
            {
                MessageBox.Show("Не удалось найти доступный сервер", "Предупреждение");
                return;
            }
            if (Servers[best].Ping > Config.MaxPingThreshold)
            {
                MessageBox.Show($"Лучший сервер имеет высокий пинг ({Servers[best].Ping}ms)", "Предупреждение");
                return;
            }
            SelectServer(best);
            if (ConnectionState == Models.ConnectionState.Disconnected)
                Connect();
        }

        public void ToggleConnection()
        {
            if (ConnectionState == Models.ConnectionState.Connected)
                Disconnect();
            else if (ConnectionState is Models.ConnectionState.Disconnected or Models.ConnectionState.Error)
                Connect();
        }

        private async void Connect()
        {
            if (SelectedServer == null)
            {
                if (GetBool("auto_select_best", true) && Servers.Count > 0)
                {
                    AutoSelectAndConnect();
                    return;
                }
                MessageBox.Show("Выберите сервер", "Предупреждение");
                return;
            }

            var server = SelectedServer;
            if (ConnectionMode == ConnectionMode.Tun)
            {
                bool isAdmin = false;
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { }
                if (!isAdmin)
                {
                    var elevate = MessageBox.Show(
                        "Для работы режима TUN (wintun) требуются права администратора.\n\nПерезапустить Hivox от имени администратора?",
                        "Режим TUN",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (elevate == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Settings["use_tun_mode"] = true;
                            SaveData();
                            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                            if (!string.IsNullOrWhiteSpace(exePath))
                            {
                                Helpers.SingleInstance.Release();
                                Process.Start(new ProcessStartInfo(exePath)
                                {
                                    UseShellExecute = true,
                                    Verb = "runas"
                                });
                                Application.Current.Shutdown();
                                return;
                            }
                        }
                        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                        {
                            Helpers.SingleInstance.EnsureSingleInstance("Hivox_VPN_v2");
                            StatusText = "Повышение прав отменено (UAC)";
                        }
                        catch (Exception ex)
                        {
                            Helpers.SingleInstance.EnsureSingleInstance("Hivox_VPN_v2");
                            MessageBox.Show($"Не удалось перезапустить от имени администратора:\n{ex.Message}", "Режим TUN", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    return;
                }
            }

            if (CoreType == CoreType.Xray && server.Protocol is "Hysteria" or "Hysteria2" or "TUIC")
            {
                CoreType = CoreType.SingBox;
                Logger.Log($"Auto-switching to Sing-box for {server.Protocol}");
            }
            else if (CoreType == CoreType.SingBox && server.Transport == "XHTTP")
            {
                CoreType = CoreType.Xray;
                Logger.Log($"Auto-switching to Xray for {server.Protocol} + {server.Transport}");
            }

            ApplyRuntimeSettings();
            if (!string.IsNullOrWhiteSpace(server.SubscriptionUrl) &&
                SubscriptionMetas.TryGetValue(server.SubscriptionUrl, out var subMetaOverride) &&
                !string.IsNullOrWhiteSpace(subMetaOverride.CustomDns))
            {
                Config.CustomDns = subMetaOverride.CustomDns.Trim();
            }
            var config = ConfigBuilder.BuildConfig(server, GameMode, ConnectionMode, CoreType,
                GetBool("mux_enabled", true), GetString("preferred_ip", "auto"),
                GetBool("allow_lan", false), GetBool("fragmentation", false),
                GetBool("http_enabled", true), GetBool("socks_enabled", true));
            if (config == null)
            {
                MessageBox.Show($"Не удалось создать конфигурацию для {server.Protocol}", "Ошибка");
                return;
            }

            CheckPorts();
            string configFile = Path.Combine(_appDir, "config_v2.json");
            File.WriteAllText(configFile, config.ToString(Formatting.Indented));

            string coreName, corePath;
            string binDir = AppDomain.CurrentDomain.BaseDirectory;
            if (ConnectionMode == ConnectionMode.Tun)
                ProcessManager.ResolveOrExtractCore("wintun.dll", binDir);

            if (CoreType == CoreType.SingBox)
            {
                coreName = "Sing-box";
                corePath = ProcessManager.ResolveOrExtractCore("sing-box.exe", binDir);
            }
            else
            {
                coreName = "Xray";
                corePath = ProcessManager.ResolveOrExtractCore("xray.exe", binDir);
            }
            if (!File.Exists(corePath))
            {
                MessageBox.Show($"{coreName} не найден! Переустановите приложение или поместите {Path.GetFileName(corePath)} рядом с программой.", "Ошибка");
                return;
            }

            string tunBridgePath = "";
            string tunBridgeConfig = "";
            if (ConnectionMode == ConnectionMode.Tun && CoreType == CoreType.Xray)
            {
                tunBridgePath = ProcessManager.ResolveOrExtractCore("sing-box.exe", binDir);
                if (!File.Exists(tunBridgePath))
                {
                    MessageBox.Show("Для работы TUN на ядре Xray требуется sing-box.exe.", "Ошибка");
                    return;
                }
                var bridgeCfg = ConfigBuilder.BuildXrayTunBridgeConfig(server, GetString("preferred_ip", "auto"), GameMode);
                tunBridgeConfig = Path.Combine(_appDir, "tun_bridge_v2.json");
                File.WriteAllText(tunBridgeConfig, bridgeCfg.ToString(Formatting.Indented));
            }

            ConnectionState = Models.ConnectionState.Connecting;
            StatusText = "Подключение...";

            try
            {
                if (CoreType == CoreType.SingBox)
                    _coreProcess = ProcessManager.StartSingbox(configFile, corePath);
                else
                    _coreProcess = ProcessManager.StartXray(configFile, corePath);

                await Task.Delay(450);

                if (_coreProcess.HasExited)
                {
                    string errDetail = string.IsNullOrEmpty(ProcessManager.LastCoreError) ? "" : $"\n{ProcessManager.LastCoreError}";
                    throw new Exception($"{coreName} завершился сразу после запуска.{errDetail}");
                }

                if (ConnectionMode == ConnectionMode.Tun && CoreType == CoreType.Xray)
                {
                    _tunBridgeProcess = ProcessManager.StartSingbox(tunBridgeConfig, tunBridgePath);
                    await Task.Delay(450);
                    if (_tunBridgeProcess.HasExited)
                    {
                        string errDetail = string.IsNullOrEmpty(ProcessManager.LastCoreError) ? "" : $"\n{ProcessManager.LastCoreError}";
                        throw new Exception($"TUN-мост завершился сразу после запуска.{errDetail}");
                    }
                }

                if (ConnectionMode == ConnectionMode.Proxy)
                {
                    ProxyManager.SetSystemProxy(true, Config.ProxyPort);
                    _proxyEnabled = true;
                }
                else
                {
                    ProxyManager.SetSystemProxy(false, Config.ProxyPort);
                    _proxyEnabled = false;
                }
                ConnectionState = Models.ConnectionState.Connected;
                _connectionStartTime = DateTime.Now;
                StatusText = GameMode ? "Подключено • Игровой" : "Подключено";
                var country = !string.IsNullOrWhiteSpace(server.CountryName) && server.CountryName != "Unknown"
                    ? server.CountryName
                    : "";
                ServerLabel = string.IsNullOrEmpty(country)
                    ? server.Name
                    : $"{server.Name} • {country}";
                _trayHelper?.UpdateState(true);
                _trayHelper?.ShowNotification("Hivox", $"Подключено к {server.Name}");
                ConnectionHistory.Add(new Dictionary<string, string>
                {
                    ["date"] = DateTime.Now.ToString("MM-dd HH:mm"),
                    ["server"] = server.Name,
                    ["duration"] = "00:00:00",
                    ["mode"] = ConnectionMode.ToString().ToLower(),
                    ["core"] = CoreType.ToString().ToLower()
                });
                SaveData();

                _connCts?.Cancel();
                _connCts?.Dispose();
                _connCts = new CancellationTokenSource();
                var connToken = _connCts.Token;

                _ = Task.Run(() => MonitorTraffic(connToken), connToken);
                _ = Task.Run(() => DetectIp(connToken), connToken);
                _ = Task.Run(() => VerifyProxy(server.Name, connToken), connToken);
                _ = Task.Run(() => HealthCheckLoop(connToken), connToken);
            }
            catch (Exception ex)
            {
                KillProcessSafe(ref _tunBridgeProcess);
                KillProcessSafe(ref _coreProcess);
                ConnectionState = Models.ConnectionState.Error;
                StatusText = "Ошибка подключения";
                MessageBox.Show($"Не удалось подключиться:\n{ex.Message}", "Ошибка");
            }
        }

        private static void KillProcessSafe(ref Process proc)
        {
            if (proc == null) return;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
            }
            catch { }
            try { proc.Dispose(); } catch { }
            proc = null;
        }

        public async void Disconnect()
        {
            ConnectionState = Models.ConnectionState.Disconnecting;
            _connCts?.Cancel();
            if (_proxyEnabled)
            {
                ProxyManager.SetSystemProxy(false, Config.ProxyPort);
                _proxyEnabled = false;
            }
            KillProcessSafe(ref _tunBridgeProcess);
            KillProcessSafe(ref _coreProcess);
            ConnectionState = Models.ConnectionState.Disconnected;
            StatusText = "Не подключено";
            TimerText = "";
            IpText = "";
            DownloadSpeedText = "0 B/s";
            UploadSpeedText = "0 B/s";
            TotalTransferredText = "0 B";
            _trayHelper?.UpdateState(false);
        }

        private async Task VerifyProxy(string serverName, CancellationToken ct)
        {
            try { await Task.Delay(1500, ct); } catch (OperationCanceledException) { return; }
            var testUrls = new[] { "http://www.gstatic.com/generate_204", "https://www.google.com/generate_204", "http://connectivitycheck.platform.hicloud.com/generate_204" };
            foreach (var url in testUrls)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var handler = new HttpClientHandler { Proxy = Config.CreateLocalHttpProxy(), UseProxy = true };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                    var response = await client.GetAsync(url, ct);
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
                        return;
                }
                catch (OperationCanceledException) { return; }
                catch { }
            }
            if (!ct.IsCancellationRequested && ConnectionState == Models.ConnectionState.Connected)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _trayHelper?.ShowNotification("Hivox", $"Проверьте подключение к {serverName}");
                });
            }
        }

        private async Task HealthCheckLoop(CancellationToken ct)
        {
            int failures = 0;
            while (!ct.IsCancellationRequested && !_cts.IsCancellationRequested && ConnectionState == Models.ConnectionState.Connected)
            {
                try { await Task.Delay(Config.HealthCheckInterval * 2000, ct); }
                catch (OperationCanceledException) { break; }
                try
                {
                    var handler = new HttpClientHandler { Proxy = Config.CreateLocalHttpProxy(), UseProxy = true };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                    var response = await client.GetAsync("https://www.google.com/generate_204", ct);
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
                        failures = 0;
                    else
                        failures++;
                }
                catch (OperationCanceledException) { break; }
                catch { failures++; }
                if (failures >= 5 && Config.AutoReconnect && !ct.IsCancellationRequested)
                {
                    Logger.Log("Health check detected connection loss — reconnecting");
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            try { Disconnect(); } catch { }
                            Task.Delay(1500).ContinueWith(_ =>
                            {
                                try { Application.Current.Dispatcher.Invoke(Connect); } catch { }
                            });
                        });
                    }
                    catch { }
                    break;
                }
            }
        }

        private static (long Sent, long Received) GetTotalNetworkBytes()
        {
            long sent = 0, recv = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var stats = ni.GetIPStatistics();
                    sent += stats.BytesSent;
                    recv += stats.BytesReceived;
                }
            }
            catch { }
            return (sent, recv);
        }

        private async Task MonitorTraffic(CancellationToken ct)
        {
            _trafficLastTime = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
            var (lastSent, lastRecv) = GetTotalNetworkBytes();
            long sessionBytes = 0;

            while (!ct.IsCancellationRequested && !_cts.IsCancellationRequested && ConnectionState == Models.ConnectionState.Connected)
            {
                try
                {
                    double currentTime = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
                    double elapsed = Math.Max(0.2, currentTime - _trafficLastTime);

                    var proc = _coreProcess;
                    if (proc != null && !proc.HasExited)
                    {
                        var (curSent, curRecv) = GetTotalNetworkBytes();
                        long dSent = Math.Max(0, curSent - lastSent);
                        long dRecv = Math.Max(0, curRecv - lastRecv);
                        sessionBytes += dSent + dRecv;
                        double upSpeed = dSent / elapsed;
                        double downSpeed = dRecv / elapsed;
                        lastSent = curSent;
                        lastRecv = curRecv;

                        var uptime = DateTime.Now - _connectionStartTime;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TimerText = $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
                            DownloadSpeedText = NetworkUtils.FormatSpeed(downSpeed);
                            UploadSpeedText = NetworkUtils.FormatSpeed(upSpeed);
                            TotalTransferredText = NetworkUtils.FormatBytes(sessionBytes);
                            SpeedUpdated?.Invoke(upSpeed / 1024.0, downSpeed / 1024.0);
                        });
                    }
                    _trafficLastTime = currentTime;
                }
                catch { }
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        private async Task DetectIp(CancellationToken ct)
        {
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var handler = new HttpClientHandler { Proxy = Config.CreateLocalHttpProxy(), UseProxy = true };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                    var response = await client.GetStringAsync("https://api.ipify.org?format=json", ct);
                    var json = JObject.Parse(response);
                    string ip = json["ip"]?.ToString();
                    if (!string.IsNullOrEmpty(ip))
                    {
                        Application.Current.Dispatcher.Invoke(() => IpText = $"IP: {ip}");
                        return;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch { }
                try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { return; }
            }
        }

        private void StartBackgroundTasks()
        {
            Task.Run(async () =>
            {
                await Task.Delay(8000);
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        if (Servers.Count > 0 && ConnectionState == Models.ConnectionState.Disconnected)
                        {
                            var list = Application.Current.Dispatcher.Invoke(() => Servers.ToList());
                            await NetworkUtils.PingServersParallelAsync(list);
                        }
                    }
                    catch { }
                    await Task.Delay(Config.PingInterval * 1000);
                }
            });

            // Auto-update subscriptions
            if (GetBool("auto_update", true))
            {
                Task.Run(async () =>
                {
                    int intervalHours = Math.Max(1, GetInt("update_interval", 6));
                    await Task.Delay(TimeSpan.FromHours(intervalHours));
                    while (!_cts.IsCancellationRequested)
                    {
                        if (!GetBool("auto_update", true)) break;
                        try
                        {
                            Application.Current.Dispatcher.Invoke(UpdateSubscriptions);
                        }
                        catch { }
                        await Task.Delay(TimeSpan.FromHours(intervalHours));
                    }
                });
            }
        }

        public void CheckPorts()
        {
            foreach (int port in new[] { Config.SocksPort, Config.ProxyPort })
            {
                try
                {
                    using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    sock.ReceiveTimeout = 300;
                    if (sock.BeginConnect("127.0.0.1", port, null, null).AsyncWaitHandle.WaitOne(300))
                    {
                        ProcessManager.KillProcess("xray.exe");
                        ProcessManager.KillProcess("sing-box.exe");
                        return;
                    }
                }
                catch { }
            }
        }

        // Dialog methods
        private void ShowSubscriptionsDialog()
        {
            var dialog = new Views.SubscriptionDialog(this);
            dialog.ShowDialog();
        }

        private void ShowMassImportDialog()
        {
            var dialog = new Views.MassImportDialog(this);
            dialog.ShowDialog();
        }

        public async Task<(bool Success, int Count, string Message)> AddAndLoadSubscriptionAsync(string rawUrl)
        {
            string url = rawUrl?.Trim() ?? "";
            if (string.IsNullOrEmpty(url))
                return (false, 0, "Введите ссылку подписки");
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return (false, 0, "Ссылка должна начинаться с http:// или https://");
            if (Subscriptions.Contains(url))
                return (false, 0, "Эта подписка уже добавлена");

            try
            {
                bool sendHwid = GetBool("send_hwid", true);
                int timeout = GetInt("request_timeout", 15);
                var (loaded, meta) = await SubscriptionManager.LoadSubscriptionWithMetadata(url, sendHwid, timeout);
                if (loaded.Count == 0)
                    return (false, 0, "Подписка не содержит поддерживаемых серверов");

                Subscriptions.Add(url);
                var existingKeys = new HashSet<string>(Servers.Select(s => s.DedupeKey));
                bool noDupes = GetBool("no_duplicates", true);
                int added = 0;
                string subName = !string.IsNullOrWhiteSpace(meta?.ProfileTitle)
                    ? meta.ProfileTitle
                    : ServerInfo.GetSubscriptionDisplayName(url);

                foreach (var s in loaded)
                {
                    s.SubscriptionUrl = url;
                    s.SubscriptionName = subName;
                    if (!noDupes || !existingKeys.Contains(s.DedupeKey))
                    {
                        Servers.Add(s);
                        existingKeys.Add(s.DedupeKey);
                        added++;
                    }
                }

                if (meta != null)
                {
                    meta.LastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    SubscriptionMetas[url] = meta;
                    ApplySubscriptionMetadata(meta);
                }
                StatusText = $"Добавлена подписка ({added} серв.)";
                SaveData();
                PingAllServers();
                _ = Task.Run(async () =>
                {
                    if (await GeoIPService.ResolveServerCountries(Servers.ToList(), _appDir))
                        Application.Current.Dispatcher.Invoke(SaveData);
                });

                return (true, added, $"Подписка добавлена, загружено серверов: {added}");
            }
            catch (Exception ex)
            {
                return (false, 0, $"Ошибка загрузки: {ex.Message}");
            }
        }

        public async Task<(bool Success, int Count, string Message)> RefreshSubscriptionsAsync()
        {
            if (Subscriptions.Count == 0)
                return (false, 0, "Список подписок пуст");

            StatusText = "Обновление подписок...";
            bool sendHwid = GetBool("send_hwid", true);
            int timeout = GetInt("request_timeout", 15);
            var newServers = new List<ServerInfo>();
            SubscriptionMetadata latestMeta = null;

            foreach (var url in Subscriptions.ToList())
            {
                try
                {
                    var (srv, meta) = await SubscriptionManager.LoadSubscriptionWithMetadata(url, sendHwid, timeout);
                    if (meta != null)
                    {
                        SubscriptionMetas.TryGetValue(url, out var oldMeta);
                        PreserveMetaOverrides(oldMeta, meta);
                        meta.LastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        SubscriptionMetas[url] = meta;
                        latestMeta = meta;
                    }
                    string subName = ResolveSubDisplayName(url, meta ?? (SubscriptionMetas.TryGetValue(url, out var m2) ? m2 : null));
                    foreach (var s in srv)
                    {
                        s.SubscriptionUrl = url;
                        s.SubscriptionName = subName;
                    }
                    newServers.AddRange(srv);
                }
                catch { }
            }

            if (newServers.Count > 0)
            {
                if (GetBool("no_duplicates", true))
                    newServers = newServers.GroupBy(s => s.DedupeKey).Select(g => g.First()).ToList();
                newServers = ServerParser.DeduplicateNames(newServers);

                var prevServer = SelectedServer;
                Servers.Clear();
                foreach (var s in newServers) Servers.Add(s);
                RestoreSelectedServer(prevServer);
                if (latestMeta != null) ApplySubscriptionMetadata(latestMeta);
                StatusText = $"Обновлено: {newServers.Count} серверов";
                SaveData();
                PingAllServers();
                _ = Task.Run(async () =>
                {
                    if (await GeoIPService.ResolveServerCountries(Servers.ToList(), _appDir))
                        Application.Current.Dispatcher.Invoke(SaveData);
                });
                if (GetBool("update_notify", false))
                    _trayHelper?.ShowNotification("Hivox", $"Обновлено {newServers.Count} серверов");
                return (true, newServers.Count, $"Загружено серверов: {newServers.Count}");
            }

            StatusText = "Не удалось обновить подписки";
            return (false, 0, "Не удалось загрузить серверы по подпискам");
        }

        private async void UpdateSubscriptions()
        {
            if (Subscriptions.Count == 0)
            {
                ShowSubscriptionsDialog();
                return;
            }
            await RefreshSubscriptionsAsync();
        }

        private void TestServer(ServerInfo server)
        {
            if (server == null)
            {
                MessageBox.Show("Выберите сервер", "Проверка сервера");
                return;
            }
            StatusText = $"Проверка {server.Name}...";
            Task.Run(async () =>
            {
                int currentPing = await NetworkUtils.PingServerAsync(server);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (currentPing > 0)
                    {
                        server.Ping = currentPing;
                        StatusText = $"{server.Name} — {currentPing}ms";
                    }
                    else
                    {
                        server.Ping = -1;
                        StatusText = $"{server.Name} — недоступен";
                    }
                });
            });
        }

        public void ConnectToServer(ServerInfo server)
        {
            if (server == null) return;
            var idx = Servers.IndexOf(server);
            if (idx >= 0)
            {
                SelectServer(idx);
                if (ConnectionState == Models.ConnectionState.Connected)
                {
                    Disconnect();
                    Task.Delay(600).ContinueWith(_ => Application.Current.Dispatcher.Invoke(Connect));
                }
                else if (ConnectionState is Models.ConnectionState.Disconnected or Models.ConnectionState.Error)
                {
                    Connect();
                }
            }
        }

        public void CopyServerUri(ServerInfo server)
        {
            if (server == null || string.IsNullOrEmpty(server.Uri)) return;
            try
            {
                System.Windows.Clipboard.SetText(server.Uri);
            }
            catch { }
        }

        public void DeleteServer(ServerInfo server)
        {
            if (server == null) return;
            var result = MessageBox.Show($"Удалить сервер {server.Name}?", "Удаление",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                int idx = Servers.IndexOf(server);
                bool wasSelected = idx >= 0 && idx == SelectedServerIndex;
                Servers.Remove(server);
                if (Servers.Count == 0)
                    SelectedServerIndex = -1;
                else if (wasSelected || SelectedServerIndex >= Servers.Count)
                    SelectedServerIndex = Math.Min(Math.Max(0, SelectedServerIndex), Servers.Count - 1);
                SaveData();
            }
        }

        public void SortByName()
        {
            var current = SelectedServer;
            var sorted = Servers.OrderBy(s => s.Name).ToList();
            Servers.Clear();
            foreach (var s in sorted) Servers.Add(s);
            if (current != null) SelectedServerIndex = Servers.IndexOf(current);
        }

        private void OnConnectionStateChanged()
        {
            _trayHelper?.UpdateState(ConnectionState == Models.ConnectionState.Connected);
        }

        public void Cleanup()
        {
            _connCts?.Cancel();
            _cts?.Cancel();
            Disconnect();
            SingleInstance.Release();
            _trayHelper?.Dispose();
        }
    }

    public static class Extensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
        {
            return dict.TryGetValue(key, out var val) ? val : defaultValue;
        }

        public static IEnumerable<TSource> TakeLastCustom<TSource>(this IEnumerable<TSource> source, int count)
        {
            var list = new List<TSource>(count);
            foreach (var item in source)
            {
                list.Add(item);
                if (list.Count > count) list.RemoveAt(0);
            }
            return list;
        }
    }
}
