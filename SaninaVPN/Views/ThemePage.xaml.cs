using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hivox.Models;

namespace Hivox.Views
{
    public partial class ThemePage : UserControl
    {
        private readonly MainWindow _mainWindow;

        public ThemePage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            DarkRadio.IsChecked = vm.Theme == AppTheme.Dark;
            BlackRadio.IsChecked = vm.Theme == AppTheme.Black;
            LightRadio.IsChecked = vm.Theme == AppTheme.Light;
        }

        private void Dark_Click(object sender, MouseButtonEventArgs e)
        {
            DarkRadio.IsChecked = true;
            (_mainWindow.DataContext as ViewModels.MainViewModel)?.SetTheme(AppTheme.Dark);
        }

        private void Black_Click(object sender, MouseButtonEventArgs e)
        {
            BlackRadio.IsChecked = true;
            (_mainWindow.DataContext as ViewModels.MainViewModel)?.SetTheme(AppTheme.Black);
        }

        private void Light_Click(object sender, MouseButtonEventArgs e)
        {
            LightRadio.IsChecked = true;
            (_mainWindow.DataContext as ViewModels.MainViewModel)?.SetTheme(AppTheme.Light);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm != null)
            {
                var theme = BlackRadio.IsChecked == true ? AppTheme.Black
                    : LightRadio.IsChecked == true ? AppTheme.Light
                    : AppTheme.Dark;
                vm.SetTheme(theme);
            }
            _mainWindow.SettingsBack();
        }
    }
}
