using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Hivox.Services;

namespace Hivox.Views
{
    public partial class SettingsMainPage : UserControl
    {
        private readonly MainWindow _mainWindow;

        public SettingsMainPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            Loaded += OnLoaded;

            ThemeRow.Click += (_, _) => _mainWindow.NavigateTo(new ThemePage(mainWindow));
            ConnectionRow.Click += (_, _) => _mainWindow.NavigateTo(new ConnectionSettingsPage(mainWindow));
            TunnelRow.Click += (_, _) => _mainWindow.NavigateTo(new TunnelSettingsPage(mainWindow));
            ProcessRulesRow.Click += (_, _) => _mainWindow.NavigateTo(new ProcessRulesPage(mainWindow));
            CoreRow.Click += (_, _) => _mainWindow.NavigateTo(new CoreTypePage(mainWindow));
            IpTypeRow.Click += (_, _) => _mainWindow.NavigateTo(new IpTypePage(mainWindow));
            InboundsRow.Click += (_, _) => _mainWindow.NavigateTo(new InboundsPage(mainWindow));
            GeoDataRow.Click += (_, _) => _mainWindow.NavigateTo(new GeoDataPage(mainWindow));
            SubsRow.Click += (_, _) => _mainWindow.NavigateTo(new SubscriptionSettingsPage(mainWindow));
            PingRow.Click += (_, _) => _mainWindow.NavigateTo(new PingSettingsPage(mainWindow));
            BackupRow.Click += (_, _) => HandleBackup();
            UrlSchemesRow.Click += async (_, _) => await HandleUrlSchemesAsync();
            ConnectionCheckRow.Click += (_, _) => _mainWindow.NavigateTo(new ConnectionCheckPage(mainWindow));
            DiagnosticsRow.Click += (_, _) => _mainWindow.NavigateTo(new DiagnosticsPage(mainWindow));
            LogsRow.Click += (_, _) => OpenLogsFolder();
            ResetRow.Click += (_, _) => ConfirmReset();
            AboutRow.Click += (_, _) => _mainWindow.NavigateTo(new AboutPage(mainWindow));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            ThemeRow.ValueText = vm.ThemeDisplayName;
            ConnectionRow.ValueText = vm.GetBool("bypass_ru", true) ? "Обход РФ" : "Весь трафик";
            TunnelRow.ValueText = $"{vm.GetString("custom_dns", "1.1.1.1")} · MTU {vm.GetInt("tun_mtu", 1420)}";
            bool procEnabled = vm.GetBool("process_rules_enabled", false);
            var procList = vm.GetString("process_rules_list", "")
                .Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            ProcessRulesRow.ValueText = !procEnabled || procList.Length == 0
                ? "Выкл"
                : (vm.GetString("process_rules_mode", "bypass") == "only_vpn"
                    ? $"Только VPN ({procList.Length})"
                    : $"Напрямую ({procList.Length})");
            CoreRow.ValueText = vm.CoreType == Models.CoreType.SingBox ? "Sing-box" : "Xray";

            var ipType = vm.GetString("preferred_ip", "auto");
            IpTypeRow.ValueText = ipType == "auto" ? "Auto" : ipType.ToUpper();

            bool httpOn = vm.GetBool("http_enabled", true);
            bool socksOn = vm.GetBool("socks_enabled", true);
            InboundsRow.ValueText = httpOn || socksOn
                ? $"HTTP:{vm.GetInt("http_port", 10809)} / SOCKS:{vm.GetInt("socks_port", 10808)}"
                : "Отключено";

            var (geoIp, geoSite) = GeoDataUpdater.GetLocalStatus(vm.AppDir);
            GeoDataRow.ValueText = geoIp.Exists && geoSite.Exists ? "Актуально" : "Встроенная";

            PingRow.ValueText = vm.GetString("ping_type", "proxy") switch
            {
                "tcp" or "icmp" => "TCP",
                "http_get" => "HTTP GET",
                "http_head" => "HTTP HEAD",
                _ => "Real Ping"
            };

            UrlSchemesRow.ValueText = ViewModels.MainViewModel.IsUrlSchemeRegistered() ? "Активно" : "hivox://";
        }

        private void HandleBackup()
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            var choice = MessageBox.Show(
                "Создать резервную копию (Да) или восстановить из файла (Нет)?",
                "Резервное копирование",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Yes)
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Сохранить резервную копию Hivox",
                    Filter = "JSON файлы (*.json)|*.json",
                    FileName = $"hivox-backup-{DateTime.Now:yyyy-MM-dd}.json"
                };
                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, vm.ExportBackupJson());
                    MessageBox.Show("Резервная копия успешно сохранена.", "Готово");
                }
            }
            else if (choice == MessageBoxResult.No)
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Выберите файл резервной копии Hivox",
                    Filter = "JSON файлы (*.json)|*.json"
                };
                if (dlg.ShowDialog() == true)
                {
                    string json = File.ReadAllText(dlg.FileName);
                    if (vm.ImportBackupJson(json))
                    {
                        MessageBox.Show("Настройки, подписки и серверы восстановлены.", "Готово");
                        OnLoaded(this, new RoutedEventArgs());
                    }
                    else
                    {
                        MessageBox.Show("Файл не является корректной резервной копией Hivox.", "Ошибка");
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task HandleUrlSchemesAsync()
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            bool registered = ViewModels.MainViewModel.IsUrlSchemeRegistered();
            var choice = MessageBox.Show(
                registered
                    ? "Протокол hivox:// уже зарегистрирован в Windows.\n\nДа — перерегистрировать hivox://\nНет — импортировать ссылку из буфера обмена"
                    : "Зарегистрировать протокол hivox:// в Windows (Да) или импортировать ссылку из буфера обмена (Нет)?",
                "Схемы URL (hivox://)",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (choice == MessageBoxResult.Yes)
            {
                bool ok = ViewModels.MainViewModel.RegisterUrlScheme(true);
                UrlSchemesRow.ValueText = ok ? "Активно" : "hivox://";
                MessageBox.Show(
                    ok ? "Протокол hivox:// успешно зарегистрирован для текущего пользователя." : "Не удалось зарегистрировать протокол.",
                    "Схемы URL");
            }
            else if (choice == MessageBoxResult.No)
            {
                string clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                if (string.IsNullOrWhiteSpace(clip))
                {
                    MessageBox.Show("Буфер обмена пуст. Скопируйте ссылку hivox://, https:// или vless://.", "Импорт");
                    return;
                }
                var (ok, msg) = await vm.HandleDeepLinkAsync(clip);
                MessageBox.Show(msg, ok ? "Импорт выполнен" : "Ошибка импорта");
            }
        }

        private void OpenLogsFolder()
        {
            var logFile = Logger.LogFile;
            if (File.Exists(logFile))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logFile}\"");
            else
                System.Diagnostics.Process.Start("explorer.exe", Logger.LogDirectory);
        }

        private void ConfirmReset()
        {
            var result = MessageBox.Show("Сбросить все настройки? Приложение перезапустится.", "Сброс",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
                vm?.ResetSettings();
            }
        }
    }
}
