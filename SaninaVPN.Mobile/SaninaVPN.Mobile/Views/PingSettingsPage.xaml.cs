using Hivox.Mobile.ViewModels;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class PingSettingsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private string _pingType = "tcp";
    private string _pingDisplay = "time";
    private int _timeout = 3;
    private bool _isNavigating;

    public PingSettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        Loaded += (_, _) =>
        {
            _pingType = vm.GetString("ping_type", "tcp");
            _pingDisplay = vm.GetString("ping_display", "time");
            _timeout = Math.Clamp(vm.GetInt("ping_timeout", 3), 1, 15);
            TestUrlEntry.Text = vm.GetString("ping_test_url", "https://www.gstatic.com/generate_204");
            UpdateUi();
        };
    }

    private void UpdateUi()
    {
        Color Resolve(string key, string fallback) =>
            Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Color.FromArgb(fallback);

        var active = Resolve("M3Primary", "#7C4DFF");
        var inactive = Resolve("M3SurfaceContainerHigh", "#2A1B3D");
        var activeText = Resolve("M3OnPrimary", "#FFFFFF");
        var inactiveText = Resolve("M3OnSurfaceVariant", "#B9B0D8");

        void StyleBtn(Border b, bool selected)
        {
            b.BackgroundColor = selected ? active : inactive;
            if (b.Content is Label lbl) lbl.TextColor = selected ? activeText : inactiveText;
        }

        StyleBtn(ProtoProxyBtn, _pingType == "proxy");
        StyleBtn(ProtoTcpBtn, _pingType == "tcp");
        StyleBtn(ProtoGetBtn, _pingType == "http_get");
        StyleBtn(ProtoHeadBtn, _pingType == "http_head");

        StyleBtn(DispTimeBtn, _pingDisplay != "dots");
        StyleBtn(DispDotsBtn, _pingDisplay == "dots");

        TimeoutLabel.Text = $"{_timeout}s";
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }
    private void OnProtoProxyTapped(object? sender, TappedEventArgs e) { _pingType = "proxy"; UpdateUi(); }
    private void OnProtoTcpTapped(object? sender, TappedEventArgs e) { _pingType = "tcp"; UpdateUi(); }
    private void OnProtoGetTapped(object? sender, TappedEventArgs e) { _pingType = "http_get"; UpdateUi(); }
    private void OnProtoHeadTapped(object? sender, TappedEventArgs e) { _pingType = "http_head"; UpdateUi(); }
    private void OnDispTimeTapped(object? sender, TappedEventArgs e) { _pingDisplay = "time"; UpdateUi(); }
    private void OnDispDotsTapped(object? sender, TappedEventArgs e) { _pingDisplay = "dots"; UpdateUi(); }
    private void OnPresetGoogle(object? sender, EventArgs e) => TestUrlEntry.Text = "https://www.gstatic.com/generate_204";
    private void OnPresetCloudflare(object? sender, EventArgs e) => TestUrlEntry.Text = "https://cp.cloudflare.com/generate_204";
    private void OnPresetApple(object? sender, EventArgs e) => TestUrlEntry.Text = "https://captive.apple.com/hotspot-detect.html";
    private void OnTimeoutMinus(object? sender, TappedEventArgs e) { _timeout = Math.Max(1, _timeout - 1); UpdateUi(); }
    private void OnTimeoutPlus(object? sender, TappedEventArgs e) { _timeout = Math.Min(15, _timeout + 1); UpdateUi(); }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isNavigating) return;
        string testUrl = string.IsNullOrWhiteSpace(TestUrlEntry.Text)
            ? "https://www.gstatic.com/generate_204"
            : TestUrlEntry.Text.Trim();
        if (!Uri.TryCreate(testUrl, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", "Введите корректный URL (http:// или https://).", "OK");
            return;
        }

        _isNavigating = true;
        _vm.Settings["ping_type"] = _pingType;
        _vm.Settings["ping_display"] = _pingDisplay;
        _vm.Settings["ping_timeout"] = _timeout;
        _vm.Settings["ping_test_url"] = testUrl;
        _vm.ApplyRuntimeSettings();
        _vm.SaveData();
        await Shell.Current.GoToAsync("..");
    }
}
