using Hivox.Mobile.Models;
using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class ProviderSettingsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private string _subUrl = "";
    private SubscriptionMetadata _meta = new();
    private int _selectedMinutes = 120;
    private string _pingDisplayMode = "time";
    private string _pingProtocol = "tcp";
    private string _serverSort = "none";
    private bool _isNavigating;
    private bool _isDialogOpen;
    private bool _loaded;

    public ProviderSettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        _subUrl = _vm.EditingSubscriptionUrl ?? "";
        if (!string.IsNullOrEmpty(_subUrl) && _vm.SubscriptionMetadataMap.TryGetValue(_subUrl, out var existing))
        {
            _meta = new SubscriptionMetadata
            {
                ProfileTitle = existing.ProfileTitle,
                UploadBytes = existing.UploadBytes,
                DownloadBytes = existing.DownloadBytes,
                TotalBytes = existing.TotalBytes,
                ExpireUnix = existing.ExpireUnix,
                UpdateIntervalHours = existing.UpdateIntervalHours,
                LastUpdatedUnix = existing.LastUpdatedUnix,
                SupportUrl = existing.SupportUrl,
                WebPageUrl = existing.WebPageUrl,
                CustomName = existing.CustomName,
                ShowOnHome = existing.ShowOnHome,
                AlwaysExpanded = existing.AlwaysExpanded,
                UseCustomInterval = existing.UseCustomInterval,
                CustomIntervalMinutes = existing.CustomIntervalMinutes,
                CustomDns = existing.CustomDns
            };
        }
        else
        {
            _meta = new SubscriptionMetadata();
        }

        string fallbackTitle = !string.IsNullOrWhiteSpace(_meta.CustomName)
            ? _meta.CustomName
            : (!string.IsNullOrWhiteSpace(_meta.ProfileTitle)
                ? _meta.ProfileTitle
                : (string.IsNullOrEmpty(_subUrl) ? "Ручные серверы" : SubscriptionGroup.GetDisplayName(_subUrl)));

        HeaderTitleLabel.Text = fallbackTitle;
        NameEntry.Text = !string.IsNullOrWhiteSpace(_meta.CustomName) ? _meta.CustomName : _meta.ProfileTitle;
        ShowOnHomeSwitch.IsToggled = _meta.ShowOnHome;
        AlwaysExpandedSwitch.IsToggled = _meta.AlwaysExpanded;

        bool hasSupport = Uri.TryCreate(_meta.SupportUrl, UriKind.Absolute, out _);
        bool hasWeb = Uri.TryCreate(_meta.WebPageUrl, UriKind.Absolute, out _);
        SupportRow.IsVisible = hasSupport;
        WebsiteRow.IsVisible = hasWeb;
        LinksDivider.IsVisible = hasSupport && hasWeb;
        LinksSection.IsVisible = hasSupport || hasWeb;

        IntervalSection.IsVisible = !string.IsNullOrEmpty(_subUrl);
        CustomIntervalSwitch.IsToggled = _meta.UseCustomInterval;
        _selectedMinutes = _meta.CustomIntervalMinutes > 0 ? _meta.CustomIntervalMinutes : 120;
        RefreshIntervalChips();

        _pingDisplayMode = _vm.GetString("ping_display", "time");
        _pingProtocol = _vm.GetString("ping_type", "tcp");
        _serverSort = _vm.GetString("default_sort", "none");
        PingUrlEntry.Text = _vm.GetString("ping_test_url", "https://www.gstatic.com/generate_204");
        CustomDnsEntry.Text = _meta.CustomDns;

        RefreshOptionLabels();
    }

    private void RefreshOptionLabels()
    {
        PingDisplayValueLabel.Text = _pingDisplayMode == "dots" ? "Точки качества" : "Время (мс)";
        PingProtocolValueLabel.Text = _pingProtocol is "http_get" or "http_head" or "http"
            ? "Hivox Ping (HTTP 204)"
            : (_pingProtocol == "proxy" ? "Proxy" : "TCP");
        SortOrderValueLabel.Text = _serverSort switch
        {
            "ping" => "По пингу",
            "name" => "По имени",
            _ => "По умолчанию"
        };
    }

    private void RefreshIntervalChips()
    {
        bool enabled = CustomIntervalSwitch.IsToggled;
        IntervalChipsGrid.Opacity = enabled ? 1.0 : 0.45;
        IntervalChipsGrid.InputTransparent = !enabled;

        SetChipState(Chip30m, Chip30mLabel, _selectedMinutes == 30);
        SetChipState(Chip1h, Chip1hLabel, _selectedMinutes == 60);
        SetChipState(Chip2h, Chip2hLabel, _selectedMinutes == 120);
        SetChipState(Chip6h, Chip6hLabel, _selectedMinutes == 360);
        SetChipState(Chip12h, Chip12hLabel, _selectedMinutes == 720);
        SetChipState(Chip24h, Chip24hLabel, _selectedMinutes == 1440);
    }

    private static void SetChipState(Border chip, Label lbl, bool selected)
    {
        chip.SetDynamicResource(Border.BackgroundColorProperty, selected ? "M3Primary" : "M3SurfaceContainerHigh");
        chip.SetDynamicResource(Border.StrokeProperty, selected ? "M3PrimaryBrush" : "M3OutlineVariantBrush");
        lbl.SetDynamicResource(Label.TextColorProperty, selected ? "M3OnPrimary" : "M3OnSurface");
    }

    private void SelectInterval(int minutes)
    {
        _selectedMinutes = minutes;
        RefreshIntervalChips();
    }

    private void OnCustomIntervalToggled(object? sender, ToggledEventArgs e) => RefreshIntervalChips();
    private void OnChip30mTapped(object? sender, TappedEventArgs e) => SelectInterval(30);
    private void OnChip1hTapped(object? sender, TappedEventArgs e) => SelectInterval(60);
    private void OnChip2hTapped(object? sender, TappedEventArgs e) => SelectInterval(120);
    private void OnChip6hTapped(object? sender, TappedEventArgs e) => SelectInterval(360);
    private void OnChip12hTapped(object? sender, TappedEventArgs e) => SelectInterval(720);
    private void OnChip24hTapped(object? sender, TappedEventArgs e) => SelectInterval(1440);

    private async void OnSupportTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (Uri.TryCreate(_meta.SupportUrl, UriKind.Absolute, out var uri))
                await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception ex)
        {
            Logger.Error($"ProviderSettings support open error: {ex.Message}");
        }
    }

    private async void OnWebsiteTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (Uri.TryCreate(_meta.WebPageUrl, UriKind.Absolute, out var uri))
                await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception ex)
        {
            Logger.Error($"ProviderSettings website open error: {ex.Message}");
        }
    }

    private async void OnPingDisplayTapped(object? sender, TappedEventArgs e)
    {
        if (_isDialogOpen || _isNavigating) return;
        _isDialogOpen = true;
        try
        {
            string? choice = await HivoxDialog.ShowActionSheetAsync(
                "Способ отображения пинга", "Отмена", null,
                "Время (мс)", "Точки качества");
            if (choice == "Время (мс)") _pingDisplayMode = "time";
            else if (choice == "Точки качества") _pingDisplayMode = "dots";
            RefreshOptionLabels();
        }
        catch (Exception ex)
        {
            Logger.Error($"ProviderSettings ping display dialog error: {ex.Message}");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private async void OnPingProtocolTapped(object? sender, TappedEventArgs e)
    {
        if (_isDialogOpen || _isNavigating) return;
        _isDialogOpen = true;
        try
        {
            string? choice = await HivoxDialog.ShowActionSheetAsync(
                "Протокол пинга", "Отмена", null,
                "Hivox Ping (HTTP 204)", "TCP");
            if (choice == "Hivox Ping (HTTP 204)") _pingProtocol = "http_get";
            else if (choice == "TCP") _pingProtocol = "tcp";
            RefreshOptionLabels();
        }
        catch (Exception ex)
        {
            Logger.Error($"ProviderSettings ping protocol dialog error: {ex.Message}");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private async void OnSortOrderTapped(object? sender, TappedEventArgs e)
    {
        if (_isDialogOpen || _isNavigating) return;
        _isDialogOpen = true;
        try
        {
            string? choice = await HivoxDialog.ShowActionSheetAsync(
                "Список серверов", "Отмена", null,
                "По умолчанию", "По пингу", "По имени");
            if (choice == "По пингу") _serverSort = "ping";
            else if (choice == "По имени") _serverSort = "name";
            else if (choice == "По умолчанию") _serverSort = "none";
            RefreshOptionLabels();
        }
        catch (Exception ex)
        {
            Logger.Error($"ProviderSettings sort order dialog error: {ex.Message}");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _isNavigating = false;
            Logger.Error($"ProviderSettings close error: {ex.Message}");
        }
    }

    private async void OnSaveTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        try
        {
            string enteredName = (NameEntry.Text ?? "").Trim();
            _meta.CustomName = string.Equals(enteredName, _meta.ProfileTitle, StringComparison.Ordinal) ? "" : enteredName;
            _meta.ShowOnHome = ShowOnHomeSwitch.IsToggled;
            _meta.AlwaysExpanded = AlwaysExpandedSwitch.IsToggled;
            _meta.UseCustomInterval = CustomIntervalSwitch.IsToggled;
            _meta.CustomIntervalMinutes = _selectedMinutes;
            _meta.CustomDns = (CustomDnsEntry.Text ?? "").Trim();

            string pingUrl = (PingUrlEntry.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(pingUrl))
                pingUrl = "https://www.gstatic.com/generate_204";

            _vm.ApplyProviderSettings(_subUrl, _meta, _pingDisplayMode, _pingProtocol, pingUrl, _serverSort);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _isNavigating = false;
            Logger.Error($"ProviderSettings save error: {ex.Message}");
        }
    }
}
