using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Hivox.Views
{
    public partial class ProcessRulesPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private readonly List<string> _processes = new();

        private static readonly string[] Presets =
        {
            "discord.exe", "chrome.exe", "msedge.exe", "firefox.exe",
            "telegram.exe", "steam.exe", "qbittorrent.exe", "obs64.exe"
        };

        public ProcessRulesPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            EnableRulesRow.IsChecked = vm.GetBool("process_rules_enabled", false);
            string mode = vm.GetString("process_rules_mode", "bypass");
            ModeOnlyVpnRadio.IsChecked = string.Equals(mode, "only_vpn", StringComparison.OrdinalIgnoreCase);
            ModeBypassRadio.IsChecked = !ModeOnlyVpnRadio.IsChecked.GetValueOrDefault();
            UpdateModeHint();

            _processes.Clear();
            string raw = vm.GetString("process_rules_list", "");
            foreach (var item in raw.Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                AddProcessInternal(item, refreshUi: false);

            BuildPresets();
            RenderProcessList();
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e) => UpdateModeHint();

        private void UpdateModeHint()
        {
            if (ModeHintText == null) return;
            ModeHintText.Text = ModeOnlyVpnRadio.IsChecked == true
                ? "Только выбранные программы идут через VPN, весь остальной трафик — напрямую."
                : "Выбранные программы идут напрямую в обход VPN, весь остальной трафик — через VPN.";
        }

        private void ProcessInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ProcessPlaceholder.Visibility = string.IsNullOrEmpty(ProcessInputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ProcessInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddManual_Click(sender, e);
                e.Handled = true;
            }
        }

        private void AddManual_Click(object sender, RoutedEventArgs e)
        {
            string val = ProcessInputBox.Text;
            if (AddProcessInternal(val, refreshUi: true))
                ProcessInputBox.Text = "";
        }

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите исполняемый файл (.exe)",
                Filter = "Программы (*.exe)|*.exe"
            };
            if (dlg.ShowDialog() == true)
                AddProcessInternal(Path.GetFileName(dlg.FileName), refreshUi: true);
        }

        private void ToggleRunning_Click(object sender, RoutedEventArgs e)
        {
            bool show = RunningAppsPanel.Visibility != Visibility.Visible;
            RunningAppsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
                PopulateRunningApps();
        }

        private void RefreshRunning_Click(object sender, RoutedEventArgs e) => PopulateRunningApps();

        private void PopulateRunningApps()
        {
            RunningAppsWrap.Children.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<string>();

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(proc.MainWindowTitle)) continue;
                        string exe = proc.ProcessName + ".exe";
                        if (exe.Equals("Hivox.exe", StringComparison.OrdinalIgnoreCase) ||
                            exe.Equals("xray.exe", StringComparison.OrdinalIgnoreCase) ||
                            exe.Equals("sing-box.exe", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (seen.Add(exe))
                            items.Add(exe);
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }
            catch { }

            items.Sort(StringComparer.OrdinalIgnoreCase);

            if (items.Count == 0)
            {
                RunningAppsWrap.Children.Add(new TextBlock
                {
                    Text = "Нет активных оконных приложений",
                    FontSize = 11.5,
                    Foreground = TryFindResource("M3OnSurfaceVariantBrush") as Brush ?? Brushes.Gray,
                    Margin = new Thickness(4)
                });
                return;
            }

            foreach (var exe in items)
                RunningAppsWrap.Children.Add(CreateChipButton(exe));
        }

        private void BuildPresets()
        {
            PresetsWrap.Children.Clear();
            foreach (var p in Presets)
                PresetsWrap.Children.Add(CreateChipButton(p));
        }

        private Button CreateChipButton(string exe)
        {
            var btn = new Button
            {
                Style = TryFindResource("M3TonalBtn") as Style,
                Height = 28,
                Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Content = new TextBlock { Text = $"+ {exe}", FontSize = 11.5 }
            };
            btn.Click += (_, _) => AddProcessInternal(exe, refreshUi: true);
            return btn;
        }

        private bool AddProcessInternal(string raw, bool refreshUi)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string name = Path.GetFileName(raw.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !name.Contains('.'))
                name += ".exe";

            if (_processes.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
                return false;

            _processes.Add(name);
            if (refreshUi)
                RenderProcessList();
            return true;
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _processes.Clear();
            RenderProcessList();
        }

        private void RenderProcessList()
        {
            ProcessListPanel.Children.Clear();
            ListTitleText.Text = $"СПИСОК ПРОЦЕССОВ ({_processes.Count})";
            EmptyListText.Visibility = _processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearAllBtn.Visibility = _processes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var cardBg = TryFindResource("M3SurfaceContainerLowBrush") as Brush ?? Brushes.Transparent;
            var borderBrush = TryFindResource("M3OutlineVariantBrush") as Brush ?? Brushes.Transparent;
            var onSurface = TryFindResource("M3OnSurfaceBrush") as Brush ?? Brushes.White;
            var primary = TryFindResource("M3PrimaryBrush") as Brush ?? Brushes.MediumPurple;
            var circleStyle = TryFindResource("M3CircleIconBtn") as Style;
            var closeGeom = TryFindResource("IconClose") as Geometry;

            foreach (var proc in _processes.ToList())
            {
                var row = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Background = cardBg,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 8, 8, 8),
                    Margin = new Thickness(0, 3, 0, 3)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                left.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = primary,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                left.Children.Add(new TextBlock
                {
                    Text = proc,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = onSurface,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var removeBtn = new Button
                {
                    Width = 26,
                    Height = 26,
                    Style = circleStyle,
                    ToolTip = "Удалить",
                    Content = new System.Windows.Shapes.Path
                    {
                        Data = closeGeom,
                        Width = 9,
                        Height = 9,
                        Stretch = Stretch.Uniform,
                        Fill = TryFindResource("M3OnSurfaceVariantBrush") as Brush ?? Brushes.Gray
                    }
                };
                string target = proc;
                removeBtn.Click += (_, _) =>
                {
                    _processes.Remove(target);
                    RenderProcessList();
                };

                Grid.SetColumn(left, 0);
                Grid.SetColumn(removeBtn, 1);
                grid.Children.Add(left);
                grid.Children.Add(removeBtn);
                row.Child = grid;
                ProcessListPanel.Children.Add(row);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            vm.Settings["process_rules_enabled"] = EnableRulesRow.IsChecked;
            vm.Settings["process_rules_mode"] = ModeOnlyVpnRadio.IsChecked == true ? "only_vpn" : "bypass";
            vm.Settings["process_rules_list"] = string.Join("\n", _processes);

            vm.ApplyRuntimeSettings();
            vm.SaveData();

            if (vm.IsConnected && vm.ConnectionMode == Models.ConnectionMode.Tun)
                _ = vm.RestartActiveConnectionAsync();

            _mainWindow.SettingsBack();
        }
    }
}
