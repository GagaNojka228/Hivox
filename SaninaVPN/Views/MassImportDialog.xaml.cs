using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Hivox.Services;
using Hivox.ViewModels;

namespace Hivox.Views
{
    public partial class MassImportDialog : Window
    {
        private readonly MainViewModel _vm;

        public MassImportDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            Owner = Application.Current.MainWindow;

            try
            {
                string clipboard = Clipboard.GetText()?.Trim() ?? "";
                if (Config.SupportedProtocols.Any(p => clipboard.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                    clipboard.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    clipboard.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    ImportTextBox.Text = clipboard;
            }
            catch { }
        }

        private async void DoImport_Click(object sender, RoutedEventArgs e)
        {
            string text = ImportTextBox.Text;
            try
            {
                var newServers = await Task.Run(() => SubscriptionManager.ParseMassLinks(text));
                if (newServers.Count > 0)
                {
                    bool noDupes = _vm.GetBool("no_duplicates", true);
                    var existingUris = new HashSet<string>(_vm.Servers.Select(s => s.Uri));
                    int added = 0;
                    foreach (var server in newServers)
                    {
                        if (!noDupes || !existingUris.Contains(server.Uri))
                        {
                            _vm.Servers.Add(server);
                            existingUris.Add(server.Uri);
                            added++;
                        }
                    }
                    ResultLabel.Text = $"Импортировано новых серверов: {added}";
                    ResultLabel.SetResourceReference(TextBlock.ForegroundProperty, "M3GreenBrush");
                    _vm.SaveData();
                    if (added > 0)
                    {
                        _vm.PingAllServers();
                        _ = Task.Run(async () =>
                        {
                            if (await GeoIPService.ResolveServerCountries(_vm.Servers.ToList(), AppDomain.CurrentDomain.BaseDirectory))
                                Application.Current.Dispatcher.Invoke(() => _vm.SaveData());
                        });
                    }
                }
                else
                {
                    ResultLabel.Text = "Не удалось найти серверы по введённым данным";
                    ResultLabel.SetResourceReference(TextBlock.ForegroundProperty, "M3RedBrush");
                }
            }
            catch (Exception ex)
            {
                ResultLabel.Text = $"Ошибка: {ex.Message[..Math.Min(70, ex.Message.Length)]}";
                ResultLabel.SetResourceReference(TextBlock.ForegroundProperty, "M3RedBrush");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
