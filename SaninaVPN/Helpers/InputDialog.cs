using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hivox.Helpers
{
    public class InputDialog : Window
    {
        public string ResponseText { get; private set; }

        public InputDialog(string question, string title, string defaultValue = "")
        {
            Title = title;
            Width = 420;
            Height = 190;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            SetResourceReference(BackgroundProperty, "M3SurfaceDimBrush");

            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = question,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "M3OnSurfaceBrush");
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox
            {
                Text = defaultValue,
                FontSize = 12.5,
                Height = 38,
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button
            {
                Content = "Отмена",
                Width = 90,
                Height = 34,
                Style = (Style)Application.Current.FindResource("M3TonalBtn"),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancelBtn.Click += (s, e) => DialogResult = false;
            btnPanel.Children.Add(cancelBtn);

            var okBtn = new Button
            {
                Content = "OK",
                Width = 90,
                Height = 34,
                Style = (Style)Application.Current.FindResource("M3FilledBtn"),
                IsDefault = true
            };
            okBtn.Click += (s, e) => { ResponseText = textBox.Text; DialogResult = true; };
            btnPanel.Children.Add(okBtn);

            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);
            Content = grid;

            Loaded += (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };
        }
    }
}
