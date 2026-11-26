using System.Windows;
using System.Windows.Controls;

namespace Hivox.Views
{
    public partial class ConnectionSettingsPage : UserControl
    {
        private readonly MainWindow _mainWindow;

        public ConnectionSettingsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            ConnectOnOpenRow.IsChecked = vm.GetBool("connect_on_open", false);
            AutoStartRow.IsChecked = vm.GetBool("auto_start", false);
            AutoBestRow.IsChecked = vm.GetBool("auto_select_best", true);
            AutoReconnectRow.IsChecked = vm.GetBool("auto_reconnect", true);
            DisconnectOnLockRow.IsChecked = vm.GetBool("disconnect_on_lock", false);
            BypassLanRow.IsChecked = vm.GetBool("bypass_lan", true);
            BypassRuRow.IsChecked = vm.GetBool("bypass_ru", true);
            AllowLanRow.IsChecked = vm.GetBool("allow_lan", false);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            bool routingChanged =
                vm.GetBool("bypass_lan", true) != BypassLanRow.IsChecked ||
                vm.GetBool("bypass_ru", true) != BypassRuRow.IsChecked ||
                vm.GetBool("allow_lan", false) != AllowLanRow.IsChecked;

            bool autoStartChanged = vm.GetBool("auto_start", false) != AutoStartRow.IsChecked;

            vm.Settings["connect_on_open"] = ConnectOnOpenRow.IsChecked;
            vm.Settings["auto_start"] = AutoStartRow.IsChecked;
            vm.Settings["auto_select_best"] = AutoBestRow.IsChecked;
            vm.Settings["auto_reconnect"] = AutoReconnectRow.IsChecked;
            vm.Settings["disconnect_on_lock"] = DisconnectOnLockRow.IsChecked;
            vm.Settings["bypass_lan"] = BypassLanRow.IsChecked;
            vm.Settings["bypass_ru"] = BypassRuRow.IsChecked;
            vm.Settings["allow_lan"] = AllowLanRow.IsChecked;

            if (autoStartChanged)
                vm.SetWindowsAutoStart(AutoStartRow.IsChecked);

            vm.ApplyRuntimeSettings();
            vm.SaveData();

            if (routingChanged && vm.IsConnected)
                _ = vm.RestartActiveConnectionAsync();

            _mainWindow.SettingsBack();
            await System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
