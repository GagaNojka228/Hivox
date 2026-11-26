using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hivox.Converters;

namespace Hivox.Controls
{
    public partial class PingBadge : UserControl
    {
        public static readonly DependencyProperty PingValueProperty =
            DependencyProperty.Register(nameof(PingValue), typeof(int), typeof(PingBadge),
                new PropertyMetadata(-1, OnPingChanged));

        public int PingValue
        {
            get => (int)GetValue(PingValueProperty);
            set => SetValue(PingValueProperty, value);
        }

        public PingBadge()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                PingToTextConverter.DisplayModeChanged += OnDisplayModeChanged;
                UpdateVisuals();
            };
            Unloaded += (_, _) => PingToTextConverter.DisplayModeChanged -= OnDisplayModeChanged;
        }

        private void OnDisplayModeChanged()
        {
            if (Dispatcher.CheckAccess()) UpdateVisuals();
            else Dispatcher.BeginInvoke(UpdateVisuals);
        }

        private static void OnPingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PingBadge)d).UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            int ping = PingValue;
            string mode = PingToTextConverter.DisplayMode;
            Brush activeColor = GetPingBrush(ping);
            Brush mutedColor = new SolidColorBrush(Color.FromArgb(0x45, 0x93, 0x8F, 0x99));

            int level = ping <= 0 ? 0
                : ping < 90 ? 4
                : ping < 180 ? 3
                : ping < 280 ? 2
                : 1;

            if (ping <= 0 || mode == "time")
            {
                BarsPanel.Visibility = Visibility.Collapsed;
                DotsPanel.Visibility = Visibility.Collapsed;
                PingText.Visibility = Visibility.Visible;
                PingText.Margin = new Thickness(0);
                PingText.Foreground = activeColor;
                PingText.Text = ping <= 0 ? "—" : $"{ping} ms";
                return;
            }

            if (mode == "bars")
            {
                BarsPanel.Visibility = Visibility.Visible;
                DotsPanel.Visibility = Visibility.Collapsed;
                PingText.Visibility = Visibility.Collapsed;
                ApplyLevelBrushes(level, activeColor, mutedColor);
            }
            else if (mode == "both")
            {
                BarsPanel.Visibility = Visibility.Visible;
                DotsPanel.Visibility = Visibility.Collapsed;
                PingText.Visibility = Visibility.Visible;
                PingText.Margin = new Thickness(6, 0, 0, 0);
                PingText.Foreground = activeColor;
                PingText.Text = $"{ping} ms";
                ApplyLevelBrushes(level, activeColor, mutedColor);
            }
            else // "dots"
            {
                BarsPanel.Visibility = Visibility.Collapsed;
                DotsPanel.Visibility = Visibility.Visible;
                PingText.Visibility = Visibility.Collapsed;
                Dot1.Fill = level >= 1 ? activeColor : mutedColor;
                Dot2.Fill = level >= 2 ? activeColor : mutedColor;
                Dot3.Fill = level >= 3 ? activeColor : mutedColor;
                Dot4.Fill = level >= 4 ? activeColor : mutedColor;
            }

            ToolTip = $"{ping} ms";
        }

        private void ApplyLevelBrushes(int level, Brush active, Brush muted)
        {
            Bar1.Background = level >= 1 ? active : muted;
            Bar2.Background = level >= 2 ? active : muted;
            Bar3.Background = level >= 3 ? active : muted;
            Bar4.Background = level >= 4 ? active : muted;
        }

        private static Brush GetPingBrush(int ping)
        {
            if (ping <= 0) return new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            if (ping < 110) return new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
            if (ping < 280) return new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            return new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }
}
