using System.Windows;
using System.Windows.Controls;
using Hivox.Helpers;

namespace Hivox.Views
{
    public partial class SubscriptionSettingsPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private string _sortMethod = "none";
        private bool _handlersAttached;

        public SubscriptionSettingsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            if (!_handlersAttached)
            {
                _handlersAttached = true;
                IntervalRow.Click += IntervalRow_Click;
                TimeoutRow.Click += TimeoutRow_Click;
                SortRow.Click += SortRow_Click;
            }

            AutoUpdRow.IsChecked = vm.GetBool("auto_update", true);
            IntervalRow.ValueText = vm.GetInt("update_interval", 6).ToString();
            NotifyRow.IsChecked = vm.GetBool("update_notify", false);
            TimeoutRow.ValueText = vm.GetInt("request_timeout", 9) + " сек.";
            UpdOnOpenRow.IsChecked = vm.GetBool("update_on_open", false);
            PingOnOpenRow.IsChecked = vm.GetBool("ping_on_open", true);
            ConnOnOpenRow.IsChecked = vm.GetBool("connect_on_open", false);
            NoDuplRow.IsChecked = vm.GetBool("no_duplicates", true);
            CollapseRow.IsChecked = vm.GetBool("collapse_subs", true);
            HwidRow.IsChecked = vm.GetBool("send_hwid", true);

            _sortMethod = vm.GetString("sort_method", "none");
            UpdateSortLabel();
        }

        private void UpdateSortLabel()
        {
            SortRow.ValueText = _sortMethod switch
            {
                "ping" => "По пингу",
                "alpha" => "По алфавиту",
                _ => "Без сортировки"
            };
        }

        private void IntervalRow_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Введите интервал автообновления (в часах, 1–72):", "Интервал обновления", IntervalRow.ValueText);
            if (dlg.ShowDialog() == true && int.TryParse(dlg.ResponseText?.Trim(), out int val))
            {
                IntervalRow.ValueText = System.Math.Clamp(val, 1, 72).ToString();
            }
        }

        private void TimeoutRow_Click(object sender, RoutedEventArgs e)
        {
            string current = TimeoutRow.ValueText.Replace(" сек.", "").Trim();
            var dlg = new InputDialog("Введите таймаут запроса подписки (в секундах, 3–60):", "Таймаут запроса", current);
            if (dlg.ShowDialog() == true && int.TryParse(dlg.ResponseText?.Trim(), out int val))
            {
                TimeoutRow.ValueText = System.Math.Clamp(val, 3, 60) + " сек.";
            }
        }

        private void SortRow_Click(object sender, RoutedEventArgs e)
        {
            _sortMethod = _sortMethod switch
            {
                "none" => "ping",
                "ping" => "alpha",
                _ => "none"
            };
            UpdateSortLabel();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var vm = _mainWindow.DataContext as ViewModels.MainViewModel;
            if (vm == null) return;

            vm.Settings["auto_update"] = AutoUpdRow.IsChecked;
            vm.Settings["update_interval"] = int.TryParse(IntervalRow.ValueText, out var interval) ? interval : 6;
            vm.Settings["update_notify"] = NotifyRow.IsChecked;
            vm.Settings["request_timeout"] = int.TryParse(TimeoutRow.ValueText.Replace(" сек.", ""), out var timeout) ? timeout : 9;
            vm.Settings["update_on_open"] = UpdOnOpenRow.IsChecked;
            vm.Settings["ping_on_open"] = PingOnOpenRow.IsChecked;
            vm.Settings["connect_on_open"] = ConnOnOpenRow.IsChecked;
            vm.Settings["no_duplicates"] = NoDuplRow.IsChecked;
            vm.Settings["collapse_subs"] = CollapseRow.IsChecked;
            vm.Settings["send_hwid"] = HwidRow.IsChecked;
            vm.Settings["sort_method"] = _sortMethod;

            Services.Config.RequestTimeout = vm.GetInt("request_timeout", 9);
            if (_sortMethod == "ping") vm.SortByPing();
            else if (_sortMethod == "alpha") vm.SortByName();

            vm.SaveData();
            _mainWindow.SettingsBack();
        }
    }
}
