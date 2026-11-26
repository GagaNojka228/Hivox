using System.Windows;
using Hivox.Helpers;
using Hivox.Services;

namespace Hivox
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e?.Args != null)
            {
                foreach (var arg in e.Args)
                {
                    if (string.Equals(arg?.Trim(), "--uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        UninstallHelper.RunUninstall();
                        Shutdown();
                        return;
                    }
                }
            }

            if (!SingleInstance.EnsureSingleInstance("Hivox_VPN_v2"))
            {
                MessageBox.Show("Hivox уже запущен!", "Hivox");
                Shutdown();
                return;
            }
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (Current.MainWindow?.DataContext is ViewModels.MainViewModel vm)
                vm.Cleanup();
            base.OnExit(e);
        }
    }
}
