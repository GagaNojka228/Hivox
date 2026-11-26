using System.Windows;
using System.Windows.Controls;

namespace Hivox.Views
{
    public partial class InboundsPage : UserControl
    {
        private readonly MainWindow _mainWindow;

        public InboundsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            HttpRow.IsChecked = vm.GetBool("http_enabled", true);
            SocksRow.IsChecked = vm.GetBool("socks_enabled", true);
            HttpPortBox.Text = vm.GetInt("http_port", 10809).ToString();
            SocksPortBox.Text = vm.GetInt("socks_port", 10808).ToString();

            HttpRow.CheckedChanged += (_, val) => { vm.Settings["http_enabled"] = val; };
            SocksRow.CheckedChanged += (_, val) => { vm.Settings["socks_enabled"] = val; };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            vm.Settings["http_port"] = int.TryParse(HttpPortBox.Text, out var hp) && hp is > 0 and <= 65535 ? hp : 10809;
            vm.Settings["socks_port"] = int.TryParse(SocksPortBox.Text, out var sp) && sp is > 0 and <= 65535 ? sp : 10808;
            vm.ApplyPortSettings();
            vm.SaveData();
            _mainWindow.SettingsBack();
        }
    }
}
