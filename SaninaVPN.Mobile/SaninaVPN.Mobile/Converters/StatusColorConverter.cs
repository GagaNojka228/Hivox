using System.Globalization;
using Hivox.Mobile.Models;

namespace Hivox.Mobile.Converters;

public class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isLight = Application.Current?.UserAppTheme == AppTheme.Light;
        Color green = Color.FromArgb(isLight ? "#15803D" : "#4ADE80");
        Color amber = Color.FromArgb(isLight ? "#B45309" : "#FBBF24");
        Color red = Color.FromArgb(isLight ? "#DC2626" : "#F87171");

        Color color = red;
        if (value is ConnectionState state)
        {
            color = state switch
            {
                ConnectionState.Connected => green,
                ConnectionState.Connecting or ConnectionState.Disconnecting => amber,
                _ => red
            };
        }
        else if (value is string status)
        {
            color = status switch
            {
                "Подключено" => green,
                "Подключение..." or "Отключение..." or "Переподключение..." => amber,
                _ => red
            };
        }

        if (targetType == typeof(Brush) || targetType == typeof(SolidColorBrush))
            return new SolidColorBrush(color);
        return color;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ThemeToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ThemeMode theme)
            return theme != ThemeMode.Light;
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDark)
            return isDark ? ThemeMode.Dark : ThemeMode.Light;
        return ThemeMode.Dark;
    }
}

public class PingToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = GetColor(value);
        if (targetType == typeof(Brush) || targetType == typeof(SolidColorBrush))
            return new SolidColorBrush(color);
        return color;
    }

    private static Color GetColor(object? value)
    {
        bool isLight = Application.Current?.UserAppTheme == AppTheme.Light;
        if (value is int ping)
        {
            if (ping < 0) return isLight ? Color.FromArgb("#79747E") : Color.FromArgb("#938F99");
            if (ping == 0) return isLight ? Color.FromArgb("#DC2626") : Color.FromArgb("#F87171");
            if (ping < 110) return isLight ? Color.FromArgb("#16A34A") : Color.FromArgb("#4ADE80");
            if (ping < 280) return isLight ? Color.FromArgb("#D97706") : Color.FromArgb("#FBBF24");
            return isLight ? Color.FromArgb("#DC2626") : Color.FromArgb("#F87171");
        }
        return isLight ? Color.FromArgb("#79747E") : Color.FromArgb("#938F99");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class PingToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Color c = Color.FromArgb("#18938F99");
        if (value is int ping)
        {
            if (ping <= 0) c = Color.FromArgb("#22938F99");
            else if (ping < 110) c = Color.FromArgb("#224ADE80");
            else if (ping < 280) c = Color.FromArgb("#22FBBF24");
            else c = Color.FromArgb("#22F87171");
        }
        if (targetType == typeof(Brush) || targetType == typeof(SolidColorBrush))
            return new SolidColorBrush(c);
        return c;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ProtocolToBadgeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToUpperInvariant() switch
        {
            "VLESS" or "VMESS" => Color.FromArgb("#339C7CF4"),
            "TROJAN" => Color.FromArgb("#3334D399"),
            "SHADOWSOCKS" or "SS" => Color.FromArgb("#33FBBF24"),
            "HYSTERIA2" or "HY2" or "HYSTERIA" => Color.FromArgb("#3338BDF8"),
            "TUIC" => Color.FromArgb("#33F472B6"),
            "SOCKS" => Color.FromArgb("#3360A5FA"),
            "WS" => Color.FromArgb("#2B60A5FA"),
            "GRPC" => Color.FromArgb("#2B2DD4BF"),
            "XHTTP" => Color.FromArgb("#2BFB923C"),
            "HTTP" or "H2" => Color.FromArgb("#2B818CF8"),
            "KCP" => Color.FromArgb("#2BC084FC"),
            "QUIC" => Color.FromArgb("#2B38BDF8"),
            "REALITY" => Color.FromArgb("#334ADE80"),
            "TLS" => Color.FromArgb("#2694A3B8"),
            _ => Color.FromArgb("#26938F99")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ProtocolToBadgeForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isLight = Application.Current?.UserAppTheme == AppTheme.Light;
        return value?.ToString()?.ToUpperInvariant() switch
        {
            "VLESS" or "VMESS" => Color.FromArgb(isLight ? "#5B21B6" : "#D0BCFF"),
            "TROJAN" => Color.FromArgb(isLight ? "#047857" : "#6EE7B7"),
            "SHADOWSOCKS" or "SS" => Color.FromArgb(isLight ? "#B45309" : "#FDE68A"),
            "HYSTERIA2" or "HY2" or "HYSTERIA" => Color.FromArgb(isLight ? "#0369A1" : "#7DD3FC"),
            "TUIC" => Color.FromArgb(isLight ? "#BE185D" : "#FBCFE8"),
            "SOCKS" => Color.FromArgb(isLight ? "#1D4ED8" : "#93C5FD"),
            "WS" => Color.FromArgb(isLight ? "#1D4ED8" : "#93C5FD"),
            "GRPC" => Color.FromArgb(isLight ? "#0F766E" : "#5EEAD4"),
            "XHTTP" => Color.FromArgb(isLight ? "#C2410C" : "#FDBA74"),
            "HTTP" or "H2" => Color.FromArgb(isLight ? "#4338CA" : "#A5B4FC"),
            "KCP" => Color.FromArgb(isLight ? "#7E22CE" : "#D8B4FE"),
            "QUIC" => Color.FromArgb(isLight ? "#0369A1" : "#7DD3FC"),
            "REALITY" => Color.FromArgb(isLight ? "#15803D" : "#86EFAC"),
            "TLS" => Color.FromArgb(isLight ? "#475569" : "#CBD5E1"),
            _ => Color.FromArgb(isLight ? "#49454F" : "#CAC4D0")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
