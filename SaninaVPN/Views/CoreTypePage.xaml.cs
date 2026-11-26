using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hivox.Models;

namespace Hivox.Views
{
    public partial class CoreTypePage : UserControl
    {
        private readonly MainWindow _mainWindow;

        public CoreTypePage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            XrayRadio.IsChecked = vm.CoreType == CoreType.Xray;
            SingboxRadio.IsChecked = vm.CoreType == CoreType.SingBox;
        }

        private void Xray_Click(object sender, MouseButtonEventArgs e) => XrayRadio.IsChecked = true;
        private void Singbox_Click(object sender, MouseButtonEventArgs e) => SingboxRadio.IsChecked = true;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            bool useSingbox = SingboxRadio.IsChecked == true;
            vm.CoreType = useSingbox ? CoreType.SingBox : CoreType.Xray;
            vm.Settings["use_singbox"] = useSingbox;

            vm.SaveData();
            _mainWindow.SettingsBack();
        }
    }
}
