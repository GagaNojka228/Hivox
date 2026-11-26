using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hivox.Mobile.Models;

public partial class SubscriptionGroup : ObservableObject
{
    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private string _trafficText = "";

    [ObservableProperty]
    private string _expireText = "";

    [ObservableProperty]
    private string _shortExpireText = "";

    [ObservableProperty]
    private double _trafficRatio;

    [ObservableProperty]
    private string _updateIntervalText = "";

    [ObservableProperty]
    private string _lastUpdatedText = "";

    [ObservableProperty]
    private string _supportUrl = "";

    [ObservableProperty]
    private string _webPageUrl = "";

    public bool HasTrafficInfo => !string.IsNullOrEmpty(TrafficText) || !string.IsNullOrEmpty(ExpireText);
    public bool HasTrafficText => !string.IsNullOrEmpty(TrafficText);
    public bool HasShortExpire => !string.IsNullOrEmpty(ShortExpireText);
    public bool HasProgressBar => HasTrafficText || TrafficRatio > 0;
    public bool HasMetaRow => HasShortExpire || HasTrafficText;
    public bool HasUpdateInterval => !string.IsNullOrEmpty(UpdateIntervalText);
    public bool HasSupportUrl => !string.IsNullOrWhiteSpace(SupportUrl);
    public bool HasWebPageUrl => !string.IsNullOrWhiteSpace(WebPageUrl);
    public bool HasLinksRow => HasSupportUrl || HasWebPageUrl;
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
    public string ServerCountText => $"{Servers.Count}";
    public string FooterMetaText => !string.IsNullOrEmpty(LastUpdatedText)
        ? $"{LastUpdatedText}  ·  {Servers.Count}"
        : $"{Servers.Count} серв.";

    public ObservableCollection<ServerInfo> Servers { get; set; } = new();

    public SubscriptionGroup()
    {
        Servers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ServerCountText));
            OnPropertyChanged(nameof(FooterMetaText));
        };
    }

    partial void OnTrafficTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasTrafficInfo));
        OnPropertyChanged(nameof(HasTrafficText));
        OnPropertyChanged(nameof(HasProgressBar));
        OnPropertyChanged(nameof(HasMetaRow));
    }

    partial void OnExpireTextChanged(string value) => OnPropertyChanged(nameof(HasTrafficInfo));

    partial void OnShortExpireTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasShortExpire));
        OnPropertyChanged(nameof(HasMetaRow));
    }

    partial void OnTrafficRatioChanged(double value) => OnPropertyChanged(nameof(HasProgressBar));
    partial void OnUpdateIntervalTextChanged(string value) => OnPropertyChanged(nameof(HasUpdateInterval));
    partial void OnLastUpdatedTextChanged(string value) => OnPropertyChanged(nameof(FooterMetaText));

    partial void OnSupportUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasSupportUrl));
        OnPropertyChanged(nameof(HasLinksRow));
    }

    partial void OnWebPageUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasWebPageUrl));
        OnPropertyChanged(nameof(HasLinksRow));
    }

    partial void OnUrlChanged(string value) => OnPropertyChanged(nameof(HasUrl));

    public static string GetDisplayName(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return "Ручные серверы";

            if (url.Contains('#'))
            {
                var frag = Uri.UnescapeDataString(url.Split('#').Last());
                if (!string.IsNullOrWhiteSpace(frag))
                    return frag;
            }
            var cleanUrl = url.Contains('#') ? url.Split('#')[0] : url;
            var uri = new Uri(cleanUrl);
            return uri.Host;
        }
        catch
        {
            return url.Length > 40 ? url[..40] + "..." : url;
        }
    }
}
