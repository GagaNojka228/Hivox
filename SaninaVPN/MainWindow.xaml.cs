using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Hivox.Models;
using Hivox.ViewModels;
using Hivox.Views;

namespace Hivox
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm;
        private readonly Stack<UserControl> _settingsStack = new();
        private readonly List<Expander> _groupExpanders = new();
        private bool _suppressGroupEvents;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm = DataContext as MainViewModel;
            if (_vm != null)
            {
                var trayHelper = new Helpers.SystemTrayHelper(this);
                _vm.SetTrayHelper(trayHelper);
                _vm.Servers.CollectionChanged += (_, _) => UpdateServerCount();
                _vm.SpeedUpdated += (up, down) => TrafficGraph.AddDataPoint(up, down);
                _vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.ConnectionMode))
                        SyncModeCoreButtons();
                    else if (args.PropertyName == nameof(MainViewModel.CoreType))
                        SyncModeCoreButtons();
                    else if (args.PropertyName == nameof(MainViewModel.IsConnected) && !_vm.IsConnected)
                        TrafficGraph.Clear();
                };
                var serversView = CollectionViewSource.GetDefaultView(_vm.Servers);
                if (serversView != null && serversView.GroupDescriptions.Count == 0)
                    serversView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServerInfo.GroupTitle)));

                SyncModeCoreButtons();
                Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;

                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    for (int i = 1; i < args.Length; i++)
                    {
                        string arg = args[i];
                        if (!string.IsNullOrWhiteSpace(arg) &&
                            (arg.StartsWith("hivox://", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase)))
                        {
                            _ = _vm.HandleDeepLinkAsync(arg);
                            break;
                        }
                    }
                }
            }
            UpdateServerCount();
        }

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock &&
                Services.Config.DisconnectOnLock &&
                _vm?.IsConnected == true)
            {
                Dispatcher.Invoke(() => _vm.Disconnect());
            }
        }

        private void SyncModeCoreButtons()
        {
            if (_vm == null) return;
            ProxyModeBtn.IsChecked = _vm.ConnectionMode == ConnectionMode.Proxy;
            TunModeBtn.IsChecked = _vm.ConnectionMode == ConnectionMode.Tun;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            _vm?.Cleanup();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null && _vm.GetBool("minimize_to_tray", true))
            {
                Hide();
                return;
            }
            Application.Current.Shutdown();
        }

        private void ProxyMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                bool changed = _vm.ConnectionMode != ConnectionMode.Proxy;
                _vm.ConnectionMode = ConnectionMode.Proxy;
                _vm.Settings["use_tun_mode"] = false;
                _vm.SaveData();
                if (changed && _vm.IsConnected)
                    _ = _vm.RestartActiveConnectionAsync();
            }
        }

        private void TunMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                bool changed = _vm.ConnectionMode != ConnectionMode.Tun;
                _vm.ConnectionMode = ConnectionMode.Tun;
                _vm.Settings["use_tun_mode"] = true;
                _vm.SaveData();
                if (changed && _vm.IsConnected)
                    _ = _vm.RestartActiveConnectionAsync();
            }
        }

        private void ServerListControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_vm?.SelectedServer != null)
                _vm.ConnectToServer(_vm.SelectedServer);
        }

        public void RefreshServerListView()
        {
            if (_vm == null) return;
            CollectionViewSource.GetDefaultView(_vm.Servers)?.Refresh();
        }

        private static string GetGroupName(Expander exp)
        {
            return (exp.DataContext as CollectionViewGroup)?.Name?.ToString() ?? "";
        }

        private void GroupExpander_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Expander exp) return;
            if (!_groupExpanders.Contains(exp))
                _groupExpanders.Add(exp);

            _suppressGroupEvents = true;
            try
            {
                bool searching = !string.IsNullOrEmpty(ServerSearchBox?.Text);
                exp.IsExpanded = searching || !(_vm?.IsGroupCollapsed(GetGroupName(exp)) == true);
            }
            finally
            {
                _suppressGroupEvents = false;
            }

            ApplyGroupMetadataToExpander(exp);
        }

        private void ApplyGroupMetadataToExpander(Expander exp)
        {
            if (_vm == null || exp?.Template == null) return;
            exp.ApplyTemplate();

            string groupName = GetGroupName(exp);
            var (url, meta) = _vm.GetGroupMetadata(groupName);

            if (exp.Template.FindName("GroupRefreshBtn", exp) is Button refreshBtn)
                refreshBtn.Visibility = string.IsNullOrWhiteSpace(url) ? Visibility.Collapsed : Visibility.Visible;

            if (exp.Template.FindName("SubIntervalText", exp) is TextBlock intervalTb)
            {
                string interval = meta?.FormattedUpdateInterval ?? "";
                intervalTb.Text = interval;
                intervalTb.Visibility = string.IsNullOrEmpty(interval) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (exp.Template.FindName("SubUpdatedText", exp) is TextBlock updatedTb)
            {
                string updated = meta?.FormattedLastUpdated ?? "";
                updatedTb.Text = updated;
                updatedTb.Visibility = string.IsNullOrEmpty(updated) ? Visibility.Collapsed : Visibility.Visible;
            }

            string expire = meta?.FormattedShortExpire ?? "";
            string traffic = meta?.FormattedTrafficWithInfinity ?? "";
            if (exp.Template.FindName("SubMetaRow", exp) is Grid metaRow)
            {
                bool hasMeta = !string.IsNullOrEmpty(expire) || !string.IsNullOrEmpty(traffic);
                metaRow.Visibility = hasMeta ? Visibility.Visible : Visibility.Collapsed;

                if (exp.Template.FindName("SubExpireText", exp) is TextBlock expireTb)
                {
                    expireTb.Text = expire;
                    expireTb.Visibility = string.IsNullOrEmpty(expire) ? Visibility.Collapsed : Visibility.Visible;
                }
                if (exp.Template.FindName("SubTrafficText", exp) is TextBlock trafficTb)
                {
                    trafficTb.Text = traffic;
                    trafficTb.Visibility = string.IsNullOrEmpty(traffic) ? Visibility.Collapsed : Visibility.Visible;
                }
                if (exp.Template.FindName("SubProgressBar", exp) is ProgressBar pb)
                {
                    pb.Value = meta?.TrafficRatio ?? 0.0;
                    pb.Visibility = hasMeta ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            string supportUrl = meta?.SupportUrl ?? "";
            string webUrl = meta?.WebPageUrl ?? "";
            if (exp.Template.FindName("SubLinksRow", exp) is StackPanel linksRow)
            {
                bool hasSupport = !string.IsNullOrWhiteSpace(supportUrl);
                bool hasWeb = !string.IsNullOrWhiteSpace(webUrl);
                linksRow.Visibility = (hasSupport || hasWeb) ? Visibility.Visible : Visibility.Collapsed;

                if (exp.Template.FindName("SubSupportBtn", exp) is Button supBtn)
                {
                    supBtn.Tag = supportUrl;
                    supBtn.Visibility = hasSupport ? Visibility.Visible : Visibility.Collapsed;
                }
                if (exp.Template.FindName("SubWebBtn", exp) is Button webBtn)
                {
                    webBtn.Tag = webUrl;
                    webBtn.Visibility = hasWeb ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private static Expander FindParentExpander(DependencyObject obj)
        {
            while (obj != null)
            {
                if (obj is Expander exp) return exp;
                obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        private async void RefreshGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || sender is not DependencyObject d) return;
            var exp = FindParentExpander(d);
            if (exp == null) return;
            var (url, _) = _vm.GetGroupMetadata(GetGroupName(exp));
            if (string.IsNullOrWhiteSpace(url)) return;

            if (sender is Button btn) btn.IsEnabled = false;
            try
            {
                await _vm.RefreshSingleSubscriptionAsync(url);
                foreach (var ex in _groupExpanders)
                    ApplyGroupMetadataToExpander(ex);
            }
            finally
            {
                if (sender is Button btn2) btn2.IsEnabled = true;
            }
        }

        private void PingGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || sender is not DependencyObject d) return;
            var exp = FindParentExpander(d);
            if (exp == null) return;
            _vm.PingGroupServers(GetGroupName(exp));
        }

        private void MoreGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || sender is not Button btn) return;
            var exp = FindParentExpander(btn);
            if (exp == null) return;

            string groupName = GetGroupName(exp);
            var (url, _) = _vm.GetGroupMetadata(groupName);

            var menu = new ContextMenu
            {
                Style = FindResource("M3PopupMenu") as Style,
                PlacementTarget = btn,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            var itemStyle = FindResource("M3PopupMenuItem") as Style;
            var dangerStyle = FindResource("M3PopupMenuItemDanger") as Style;
            var sepStyle = FindResource("M3PopupSeparator") as Style;
            var iconFill = TryFindResource("M3OnSurfaceVariantBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
            var dangerFill = TryFindResource("M3RedBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.IndianRed;

            var settingsItem = new MenuItem
            {
                Header = "Настройки",
                Style = itemStyle,
                Icon = new System.Windows.Shapes.Path
                {
                    Data = TryFindResource("IconSettings") as System.Windows.Media.Geometry,
                    Width = 15,
                    Height = 15,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Fill = iconFill
                }
            };
            settingsItem.Click += (_, _) => OpenSubscriptionEditPage(groupName, url);
            menu.Items.Add(settingsItem);

            var shareItem = new MenuItem
            {
                Header = "Поделиться",
                Style = itemStyle,
                Icon = new System.Windows.Shapes.Path
                {
                    Data = TryFindResource("IconShare") as System.Windows.Media.Geometry,
                    Width = 15,
                    Height = 15,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Fill = iconFill
                }
            };
            shareItem.Click += (_, _) =>
            {
                string shareText = _vm.GetGroupShareText(groupName, url);
                if (!string.IsNullOrWhiteSpace(shareText))
                {
                    try
                    {
                        Clipboard.SetText(shareText);
                        _vm.StatusText = string.IsNullOrWhiteSpace(url)
                            ? "Ссылки серверов скопированы"
                            : "Ссылка подписки скопирована";
                    }
                    catch { }
                }
            };
            menu.Items.Add(shareItem);

            menu.Items.Add(new Separator { Style = sepStyle });

            var deleteItem = new MenuItem
            {
                Header = "Удалить",
                Style = dangerStyle,
                Icon = new System.Windows.Shapes.Path
                {
                    Data = TryFindResource("IconTrash") as System.Windows.Media.Geometry,
                    Width = 15,
                    Height = 15,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Fill = dangerFill
                }
            };
            deleteItem.Click += (_, _) =>
            {
                var res = MessageBox.Show(
                    $"Удалить подписку «{groupName}» и все её серверы?",
                    "Удаление подписки",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    _vm.DeleteSubscriptionGroup(groupName, url);
                    RefreshServerListView();
                    UpdateServerCount();
                }
            };
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
        }

        private void OpenSubscriptionEditPage(string groupName, string url)
        {
            _settingsStack.Clear();
            SettingsOverlay.Visibility = Visibility.Visible;
            NavigateTo(new SubscriptionEditPage(this, groupName, url));
        }

        public void RefreshGroupHeaders()
        {
            foreach (var exp in _groupExpanders)
            {
                ApplyGroupMetadataToExpander(exp);
                if (_vm != null)
                {
                    var (_, meta) = _vm.GetGroupMetadata(GetGroupName(exp));
                    if (meta?.NeverCollapse == true)
                    {
                        _suppressGroupEvents = true;
                        try { exp.IsExpanded = true; }
                        finally { _suppressGroupEvents = false; }
                    }
                }
            }
        }

        private static void OpenSafeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void OpenGroupSupport_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
                OpenSafeUrl(url);
        }

        private void OpenGroupWeb_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
                OpenSafeUrl(url);
        }

        private void GroupExpander_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is Expander exp)
                _groupExpanders.Remove(exp);
        }

        private void GroupExpander_StateChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressGroupEvents || _vm == null || sender is not Expander exp) return;
            if (!string.IsNullOrEmpty(ServerSearchBox?.Text)) return;
            _vm.SetGroupCollapsed(GetGroupName(exp), !exp.IsExpanded);
        }

        private void ToggleAllGroups_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            bool collapseAll = false;
            foreach (var exp in _groupExpanders)
            {
                if (exp.IsExpanded)
                {
                    collapseAll = true;
                    break;
                }
            }

            _vm.SetAllGroupsCollapsed(collapseAll);
            _suppressGroupEvents = true;
            try
            {
                foreach (var exp in _groupExpanders)
                {
                    var (_, meta) = _vm.GetGroupMetadata(GetGroupName(exp));
                    exp.IsExpanded = !collapseAll || meta?.NeverCollapse == true;
                }
            }
            finally
            {
                _suppressGroupEvents = false;
            }
        }

        private void ShowSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsOverlay();
        }

        private void ShowSettingsOverlay()
        {
            _settingsStack.Clear();
            SettingsOverlay.Visibility = Visibility.Visible;
            NavigateTo(new SettingsMainPage(this));
        }

        public void NavigateTo(UserControl page)
        {
            _settingsStack.Push(page);
            SettingsContent.Content = page;
            UpdateSettingsHeaderTitle(page);
        }

        public void SettingsBack()
        {
            if (_settingsStack.Count > 0)
                _settingsStack.Pop();

            if (_settingsStack.Count > 0)
            {
                var current = _settingsStack.Peek();
                SettingsContent.Content = current;
                UpdateSettingsHeaderTitle(current);
            }
            else
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                SettingsContent.Content = null;
                SettingsTitleText.Text = "Настройки";
            }
        }

        private void UpdateSettingsHeaderTitle(UserControl page)
        {
            SettingsTitleText.Text = page switch
            {
                ThemePage => "Тема оформления",
                ConnectionSettingsPage => "Соединение и автоматизация",
                TunnelSettingsPage => "Расширенный туннель",
                ProcessRulesPage => "Правила по процессам",
                SubscriptionEditPage subPage => $"Настройки · {subPage.GroupTitle}",
                CoreTypePage => "Ядро маршрутизации",
                IpTypePage => "Предпочитаемый IP",
                InboundsPage => "Локальные Inbounds",
                GeoDataPage => "Geo-базы (SHA-256)",
                SubscriptionSettingsPage => "Подписки",
                PingSettingsPage => "Пинг и задержка",
                ConnectionCheckPage => "Проверить соединение",
                DiagnosticsPage => "Тест скорости и DNS",
                AboutPage => "О программе",
                _ => "Настройки"
            };
        }

        private void SettingsBack_Click(object sender, RoutedEventArgs e)
        {
            SettingsBack();
        }

        private void ServerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_vm == null) return;
            var rawText = ServerSearchBox.Text ?? "";
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(rawText) ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(rawText) ? Visibility.Collapsed : Visibility.Visible;

            var view = CollectionViewSource.GetDefaultView(_vm.Servers);
            var text = rawText.Trim().ToLower();
            view.Filter = item =>
            {
                if (string.IsNullOrEmpty(text)) return true;
                var s = item as ServerInfo;
                return s != null && (s.Name.ToLower().Contains(text) || s.Protocol.ToLower().Contains(text) || s.Host.ToLower().Contains(text) || s.CountryName.ToLower().Contains(text) || s.CountryBadge.ToLower().Contains(text));
            };
            view.Refresh();
            UpdateServerCount();

            _suppressGroupEvents = true;
            try
            {
                bool searching = !string.IsNullOrEmpty(rawText);
                foreach (var exp in _groupExpanders)
                    exp.IsExpanded = searching || !_vm.IsGroupCollapsed(GetGroupName(exp));
            }
            finally
            {
                _suppressGroupEvents = false;
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            ServerSearchBox.Text = "";
            ServerSearchBox.Focus();
        }

        private void CopyIp_Click(object sender, MouseButtonEventArgs e)
        {
            if (_vm == null || string.IsNullOrWhiteSpace(_vm.IpText)) return;
            try
            {
                var ipOnly = _vm.IpText.Replace("IP:", "").Trim();
                Clipboard.SetText(ipOnly);
                _vm.StatusText = "IP скопирован";
            }
            catch { }
        }

        private void UpdateServerCount()
        {
            if (_vm == null) return;
            var view = CollectionViewSource.GetDefaultView(_vm.Servers);
            int count = 0;
            if (view != null)
            {
                foreach (var _ in view) count++;
            }
            int n = count % 100;
            string suffix;
            if (n is >= 11 and <= 19)
                suffix = "серверов";
            else
            {
                int m = n % 10;
                if (m == 1) suffix = "сервер";
                else if (m >= 2 && m <= 4) suffix = "сервера";
                else suffix = "серверов";
            }
            ServerCountLabel.Text = $"{count} {suffix}";
        }
    }
}
