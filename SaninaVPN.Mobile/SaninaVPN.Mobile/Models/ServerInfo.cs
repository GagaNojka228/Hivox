using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hivox.Mobile.Models;

public class ServerInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public string Uri { get; set; } = "";
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string Host { get; set; } = "";

    private int _ping = -1;
    public int Ping
    {
        get => _ping;
        set
        {
            if (_ping != value)
            {
                _ping = value;
                Notify();
                Notify(nameof(PingText));
                Notify(nameof(PingShortText));
                Notify(nameof(HasPing));
            }
        }
    }

    public string PingText => FormatPing(_ping);
    public string PingShortText => FormatPingShort(_ping);
    public bool HasPing => _ping > 0;

    public void RefreshPingDisplay()
    {
        Notify(nameof(PingText));
        Notify(nameof(PingShortText));
    }

    public void RefreshThemeBindings()
    {
        Notify(nameof(Ping));
        Notify(nameof(Protocol));
        Notify(nameof(Transport));
        Notify(nameof(Security));
        Notify(nameof(IsSelected));
    }

    private static string FormatPingShort(int ping)
    {
        if (PingDisplayMode == "dots")
            return ping > 0 ? $"{ping}ms" : "n/a";
        if (ping > 0) return $"{ping}ms";
        if (ping == 0) return "n/a";
        return "—";
    }

    private static string FormatPing(int ping)
    {
        if (PingDisplayMode == "dots")
        {
            if (ping <= 0) return "Недоступен";
            if (ping < 110) return "Отлично";
            if (ping < 280) return "Средне";
            return "Высокий";
        }
        if (ping <= 0) return "—";
        return $"{ping} ms";
    }

    public static string PingDisplayMode { get; set; } = "time";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                Notify();
            }
        }
    }

    private string _countryCode = "";
    public string CountryCode
    {
        get => _countryCode;
        set
        {
            if (_countryCode != value)
            {
                _countryCode = value;
                Notify();
                Notify(nameof(CountryBadge));
            }
        }
    }

    private string _countryName = "";
    public string CountryName
    {
        get => _countryName;
        set
        {
            if (_countryName != value)
            {
                _countryName = value;
                Notify();
                Notify(nameof(LocationSummary));
            }
        }
    }

    private string _flag = "\U0001F310";
    public string Flag
    {
        get => _flag;
        set { if (_flag != value) { _flag = value; Notify(); } }
    }

    public int Port { get; set; } = 443;
    public string Details { get; set; } = "";
    public string Transport { get; set; } = "";
    public string Security { get; set; } = "";
    public string SubscriptionUrl { get; set; } = "";
    public string RawJsonConfig { get; set; } = "";

    public bool HasTransport => !string.IsNullOrWhiteSpace(Transport);
    public bool HasSecurity => !string.IsNullOrWhiteSpace(Security) && !Security.Equals("none", StringComparison.OrdinalIgnoreCase);
    public bool HasRawJson => !string.IsNullOrWhiteSpace(RawJsonConfig);

    public string CountryBadge => !string.IsNullOrWhiteSpace(CountryCode) && CountryCode.Length == 2
        ? CountryCode.ToUpperInvariant()
        : "GL";

    public string LocationSummary => !string.IsNullOrWhiteSpace(CountryName) && CountryName != "Unknown"
        ? $"{CountryName} · {Port}"
        : $"{Host}:{Port}";

    public string DedupeKey => !string.IsNullOrWhiteSpace(RawJsonConfig)
        ? $"json|{Uri.Trim()}|{Name}"
        : Uri.Trim();
}
