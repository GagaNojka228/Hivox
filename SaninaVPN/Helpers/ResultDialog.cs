using System.Windows;
using System.Windows.Controls;

namespace Hivox.Helpers
{
    public class ResultDialog : Window
    {
        private readonly TextBox _textBox;

        public ResultDialog(string title, string initialText)
        {
            Title = title;
            Width = 420;
            Height = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            SetResourceReference(BackgroundProperty, "M3SurfaceDimBrush");

            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "M3OnSurfaceBrush");
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            _textBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Padding = new Thickness(10),
                Text = initialText
            };
            Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            var closeBtn = new Button
            {
                Content = "Закрыть",
                Width = 110,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = (Style)Application.Current.FindResource("M3TonalBtn"),
                Margin = new Thickness(0, 12, 0, 0)
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetRow(closeBtn, 2);
            grid.Children.Add(closeBtn);
            Content = grid;
        }

        public void AppendResult(string text)
        {
            Dispatcher.Invoke(() => _textBox.AppendText(text + "\n"));
        }
    }
}
