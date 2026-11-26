using Hivox.Mobile.Services;
using Hivox.Mobile.Services.PlatformAndroid;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class GeoDataPage : ContentPage
{
    private readonly MainViewModel _vm;
    private int _intervalDays = 7;
    private bool _isLoaded;
    private bool _hasSaved;
    private bool _isNavigating;
    private CancellationTokenSource? _cts;

    public GeoDataPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hasSaved = false;
        _isNavigating = false;

        GeoAutoSwitch.IsToggled = _vm.GetBool("geo_auto_update", true);
        _intervalDays = Math.Clamp(_vm.GetInt("geo_update_interval_days", 7), 1, 90);
        UpdateIntervalLabel();
        RefreshLocalStatus();
        _isLoaded = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cts?.Cancel();
        SaveChanges();
    }

    private void RefreshLocalStatus()
    {
        var (ip, site) = GeoDataUpdater.GetLocalStatus(PlatformHelper.AppDataDir);
        GeoIpSizeLabel.Text = ip.Exists ? NetworkUtils.FormatBytes(ip.SizeBytes) : "Нет";
        GeoIpShaLabel.Text = $"SHA-256: {ip.Sha256Prefix}";
        GeoSiteSizeLabel.Text = site.Exists ? NetworkUtils.FormatBytes(site.SizeBytes) : "Нет";
        GeoSiteShaLabel.Text = $"SHA-256: {site.Sha256Prefix}";

        long lastUnix = _vm.GetLong("geo_last_updated_unix", 0);
        if (lastUnix > 0)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(lastUnix).ToLocalTime();
            LastUpdatedLabel.Text = $"Последнее обновление: {dt:dd.MM.yyyy HH:mm}";
        }
        else
        {
            LastUpdatedLabel.Text = "Последнее обновление: встроенная база APK";
        }
    }

    private void UpdateIntervalLabel()
    {
        IntervalLabel.Text = _intervalDays switch
        {
            3 => "3 дня",
            7 => "7 дней",
            14 => "14 дней",
            30 => "30 дней",
            _ => $"{_intervalDays} дн."
        };
    }

    private void SaveChanges()
    {
        if (!_isLoaded || _hasSaved) return;
        _hasSaved = true;
        _vm.Settings["geo_auto_update"] = GeoAutoSwitch.IsToggled;
        _vm.Settings["geo_update_interval_days"] = _intervalDays;
        _vm.ApplyRuntimeSettings();
        _vm.SaveData();
    }

    private async void OnIntervalTapped(object? sender, TappedEventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("Интервал обновления Geo-баз", "Отмена", null,
            "3 дня", "7 дней", "14 дней", "30 дней");
        if (action == "3 дня") _intervalDays = 3;
        else if (action == "7 дней") _intervalDays = 7;
        else if (action == "14 дней") _intervalDays = 14;
        else if (action == "30 дней") _intervalDays = 30;
        else return;

        UpdateIntervalLabel();
    }

    private async void OnUpdateClicked(object? sender, EventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        ProgressRow.IsVisible = true;
        UpdateSpinner.IsRunning = true;
        ResultBannerLabel.IsVisible = false;
        StatusLabel.Text = "Подготовка загрузки...";

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var progress = new Progress<string>(msg => StatusLabel.Text = msg);
        var (ok, message) = await _vm.UpdateGeoDataAsync(silent: true, progress: progress, ct: _cts.Token);

        UpdateSpinner.IsRunning = false;
        ProgressRow.IsVisible = false;
        UpdateBtn.IsEnabled = true;

        RefreshLocalStatus();

        ResultBannerLabel.Text = message;
        ResultBannerLabel.TextColor = ok
            ? (Application.Current?.Resources.TryGetValue("M3Green", out var g) == true && g is Color gc ? gc : Colors.LimeGreen)
            : (Application.Current?.Resources.TryGetValue("M3Red", out var r) == true && r is Color rc ? rc : Colors.Red);
        ResultBannerLabel.IsVisible = true;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        SaveChanges();
        await Shell.Current.GoToAsync("..");
    }

    private async void OnDoneTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        SaveChanges();
        await Shell.Current.GoToAsync("..");
    }
}
