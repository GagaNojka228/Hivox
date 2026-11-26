using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hivox.Converters
{
    public class PingToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int ping)
            {
                if (ping <= 0) return new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                if (ping < 110) return new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                if (ping < 280) return new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
                return new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            }
            return new SolidColorBrush(Color.FromRgb(0x93, 0x8F, 0x99));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PingToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int ping)
            {
                if (ping <= 0) return new SolidColorBrush(Color.FromArgb(0x22, 0xF8, 0x71, 0x71));
                if (ping < 110) return new SolidColorBrush(Color.FromArgb(0x22, 0x4A, 0xDE, 0x80));
                if (ping < 280) return new SolidColorBrush(Color.FromArgb(0x22, 0xFB, 0xBF, 0x24));
                return new SolidColorBrush(Color.FromArgb(0x22, 0xF8, 0x71, 0x71));
            }
            return new SolidColorBrush(Color.FromArgb(0x18, 0x93, 0x8F, 0x99));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PingToTextConverter : IValueConverter
    {
        private static string _displayMode = "time";
        public static string DisplayMode
        {
            get => _displayMode;
            set
            {
                string norm = Normalize(value);
                if (_displayMode == norm) return;
                _displayMode = norm;
                DisplayModeChanged?.Invoke();
            }
        }

        public static bool UseIconMode
        {
            get => _displayMode != "time";
            set => DisplayMode = value ? "dots" : "time";
        }

        public static event Action DisplayModeChanged;

        public static string Normalize(string value) => (value ?? "time").Trim().ToLowerInvariant() switch
        {
            "bars" => "bars",
            "both" => "both",
            "dots" or "icon" => "dots",
            _ => "time"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int ping)
            {
                if (_displayMode == "dots")
                {
                    if (ping <= 0) return "Недоступен";
                    if (ping < 110) return "Отлично";
                    if (ping < 280) return "Средне";
                    return "Высокий";
                }
                if (ping <= 0) return "—";
                return $"{ping} ms";
            }
            return "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ProtocolToBadgeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var color = value?.ToString() switch
            {
                "VLESS" or "VMess" => Color.FromArgb(0x33, 0x9C, 0x7C, 0xF4),
                "Trojan" => Color.FromArgb(0x33, 0x34, 0xD3, 0x99),
                "Shadowsocks" or "SS" => Color.FromArgb(0x33, 0xFB, 0xBF, 0x24),
                "Hysteria2" or "Hysteria" => Color.FromArgb(0x33, 0x38, 0xBD, 0xF8),
                "TUIC" => Color.FromArgb(0x33, 0xF4, 0x72, 0xB6),
                "WS" => Color.FromArgb(0x2B, 0x60, 0xA5, 0xFA),
                "GRPC" => Color.FromArgb(0x2B, 0x2D, 0xD4, 0xBF),
                "XHTTP" => Color.FromArgb(0x2B, 0xFB, 0x92, 0x3C),
                "HTTP" or "h2" => Color.FromArgb(0x2B, 0x81, 0x8C, 0xF8),
                "KCP" => Color.FromArgb(0x2B, 0xC0, 0x84, 0xFC),
                "QUIC" => Color.FromArgb(0x2B, 0x38, 0xBD, 0xF8),
                "REALITY" => Color.FromArgb(0x33, 0x4A, 0xDE, 0x80),
                "TLS" => Color.FromArgb(0x26, 0x94, 0xA3, 0xB8),
                _ => Color.FromArgb(0x26, 0x93, 0x8F, 0x99)
            };
            return new SolidColorBrush(color);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ProtocolToBadgeForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var color = value?.ToString() switch
            {
                "VLESS" or "VMess" => Color.FromRgb(0xD0, 0xBC, 0xFF),
                "Trojan" => Color.FromRgb(0x6E, 0xE7, 0xB7),
                "Shadowsocks" or "SS" => Color.FromRgb(0xFD, 0xE6, 0x8A),
                "Hysteria2" or "Hysteria" => Color.FromRgb(0x7D, 0xD3, 0xFC),
                "TUIC" => Color.FromRgb(0xFB, 0xCF, 0xE8),
                "WS" => Color.FromRgb(0x93, 0xC5, 0xFD),
                "GRPC" => Color.FromRgb(0x5E, 0xEA, 0xD4),
                "XHTTP" => Color.FromRgb(0xFD, 0xBA, 0x74),
                "HTTP" or "h2" => Color.FromRgb(0xA5, 0xB4, 0xFC),
                "KCP" => Color.FromRgb(0xD8, 0xB4, 0xFE),
                "QUIC" => Color.FromRgb(0x7D, 0xD3, 0xFC),
                "REALITY" => Color.FromRgb(0x86, 0xEF, 0xAC),
                "TLS" => Color.FromRgb(0xCB, 0xD5, 0xE1),
                _ => Color.FromRgb(0xCA, 0xC4, 0xD0)
            };
            return new SolidColorBrush(color);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
