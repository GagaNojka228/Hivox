using System;
using System.Linq;
using System.Windows;

namespace HivoxInstaller
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            bool uninstallMode = e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
            var win = new MainWindow(uninstallMode);
            MainWindow = win;
            win.Show();
        }
    }
}
