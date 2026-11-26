using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hivox.ViewModels;

namespace Hivox.Views
{
    public partial class SubscriptionDialog : Window
    {
        private readonly MainViewModel _vm;

        public SubscriptionDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            Owner = Application.Current.MainWindow;
            LoadSubscriptions();

            try
            {
                string clip = Clipboard.GetText()?.Trim() ?? "";
                if (clip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    clip.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    UrlTextBox.Text = clip;
                }
            }
            catch { }
        }

        private void LoadSubscriptions()
        {
            SubscriptionListBox.ItemsSource = null;
            SubscriptionListBox.ItemsSource = _vm.Subscriptions;
        }

        private void ShowStatus(string text, string brushKey)
        {
            StatusBanner.Text = text;
            StatusBanner.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            StatusBanner.Visibility = Visibility.Visible;
        }

        private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UrlPlaceholder.Visibility = string.IsNullOrEmpty(UrlTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                AddSubscription_Click(sender, e);
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string clip = Clipboard.GetText()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(clip))
                {
                    UrlTextBox.Text = clip;
                    UrlTextBox.CaretIndex = clip.Length;
                }
            }
            catch { }
        }

        private async void AddSubscription_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text?.Trim() ?? "";
            AddBtn.IsEnabled = false;
            RefreshAllBtn.IsEnabled = false;
            ShowStatus("Загрузка подписки и серверов...", "M3PrimaryBrush");

            var (ok, _, msg) = await _vm.AddAndLoadSubscriptionAsync(url);

            AddBtn.IsEnabled = true;
            RefreshAllBtn.IsEnabled = true;

            if (ok)
            {
                UrlTextBox.Text = "";
                LoadSubscriptions();
                ShowStatus(msg, "M3GreenBrush");
            }
            else
            {
                ShowStatus(msg, "M3RedBrush");
            }
        }

        private void CopySubscription_Click(object sender, RoutedEventArgs e)
        {
            if (SubscriptionListBox.SelectedItem is not string sub)
            {
                ShowStatus("Выберите подписку из списка", "M3AmberBrush");
                return;
            }
            try
            {
                Clipboard.SetText(sub);
                ShowStatus("Ссылка подписки скопирована", "M3GreenBrush");
            }
            catch { }
        }

        private void DeleteSubscription_Click(object sender, RoutedEventArgs e)
        {
            if (SubscriptionListBox.SelectedIndex < 0 ||
                SubscriptionListBox.SelectedIndex >= _vm.Subscriptions.Count)
            {
                ShowStatus("Выберите подписку для удаления", "M3AmberBrush");
                return;
            }
            int index = SubscriptionListBox.SelectedIndex;
            string sub = _vm.Subscriptions[index];
            string display = sub.Length > 55 ? sub[..55] + "..." : sub;
            if (MessageBox.Show($"Удалить подписку?\n{display}", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _vm.Subscriptions.RemoveAt(index);
                _vm.SaveData();
                LoadSubscriptions();
                ShowStatus("Подписка удалена", "M3OnSurfaceVariantBrush");
            }
        }

        private async void UpdateSubscriptions_Click(object sender, RoutedEventArgs e)
        {
            AddBtn.IsEnabled = false;
            RefreshAllBtn.IsEnabled = false;
            ShowStatus("Обновление подписок...", "M3PrimaryBrush");

            var (ok, _, msg) = await _vm.RefreshSubscriptionsAsync();

            AddBtn.IsEnabled = true;
            RefreshAllBtn.IsEnabled = true;
            ShowStatus(msg, ok ? "M3GreenBrush" : "M3RedBrush");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
