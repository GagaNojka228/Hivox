using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hivox.Views
{
    public partial class IpTypePage : UserControl
    {
        private readonly MainWindow _mainWindow;

        public IpTypePage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            var ipType = vm.GetString("preferred_ip", "auto");
            AutoRadio.IsChecked = ipType == "auto";
            Ipv4Radio.IsChecked = ipType == "ipv4";
            Ipv6Radio.IsChecked = ipType == "ipv6";
        }

        private void Auto_Click(object sender, MouseButtonEventArgs e) => AutoRadio.IsChecked = true;
        private void Ipv4_Click(object sender, MouseButtonEventArgs e) => Ipv4Radio.IsChecked = true;
        private void Ipv6_Click(object sender, MouseButtonEventArgs e) => Ipv6Radio.IsChecked = true;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            vm.Settings["preferred_ip"] = AutoRadio.IsChecked == true ? "auto" :
                                          Ipv4Radio.IsChecked == true ? "ipv4" : "ipv6";
            vm.SaveData();
            _mainWindow.SettingsBack();
        }
    }
}
