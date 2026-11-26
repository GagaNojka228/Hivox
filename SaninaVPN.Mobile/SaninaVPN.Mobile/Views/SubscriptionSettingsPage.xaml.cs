using Hivox.Mobile.ViewModels;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class SubscriptionSettingsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isLoading;
    private bool _hasSaved;
    private bool _isNavigating;
    private int _updateIntervalHours = 6;
    private string _customUserAgent = "v2rayNG/1.9.34";
    private string _defaultSort = "none";

    public SubscriptionSettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hasSaved = false;
        _isNavigating = false;
        LoadSettings();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SaveChanges();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            AutoUpdateSwitch.IsToggled = _vm.GetBool("auto_update", true);
            UpdateNotifySwitch.IsToggled = _vm.GetBool("update_notify", true);
            UpdateOnOpenSwitch.IsToggled = _vm.GetBool("update_on_open", true);
            PingOnOpenSwitch.IsToggled = _vm.GetBool("ping_on_open", true);
            PingOnUpdateSwitch.IsToggled = _vm.GetBool("ping_on_update", true);
            SendHwidSwitch.IsToggled = _vm.GetBool("send_hwid", true);
            NoDuplicatesSwitch.IsToggled = _vm.GetBool("no_duplicates", true);

            _updateIntervalHours = Math.Clamp(_vm.GetInt("update_interval", 6), 1, 24);
            UpdateIntervalLabel();

            _customUserAgent = _vm.GetString("custom_user_agent", "v2rayNG/1.9.34");
            if (string.IsNullOrWhiteSpace(_customUserAgent) ||
                _customUserAgent == "Hivox/2.0 (Android; Xray-core/26.3)")
            {
                _customUserAgent = "v2rayNG/1.9.34";
            }
            UserAgentLabel.Text = _customUserAgent;

            _defaultSort = _vm.GetString("default_sort", "none");
            if (string.IsNullOrWhiteSpace(_defaultSort))
                _defaultSort = "none";
            UpdateSortCheckmarks();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateIntervalLabel()
    {
        IntervalLabel.Text = _updateIntervalHours switch
        {
            1 => "1 час",
            3 => "3 часа",
            6 => "6 часов",
            12 => "12 часов",
            24 => "24 часа",
            _ => $"{_updateIntervalHours} ч"
        };
    }

    private void UpdateSortCheckmarks()
    {
        SortDefaultCheck.IsVisible = _defaultSort == "none";
        SortPingCheck.IsVisible = _defaultSort == "ping";
        SortNameCheck.IsVisible = _defaultSort == "name";
    }

    private void SaveChanges()
    {
        if (_isLoading || _hasSaved) return;
        _hasSaved = true;

        bool sortChanged = _vm.GetString("default_sort", "none") != _defaultSort;

        var s = _vm.Settings;
        s["auto_update"] = AutoUpdateSwitch.IsToggled;
        s["update_interval"] = _updateIntervalHours;
        s["update_notify"] = UpdateNotifySwitch.IsToggled;
        s["update_on_open"] = UpdateOnOpenSwitch.IsToggled;
        s["ping_on_open"] = PingOnOpenSwitch.IsToggled;
        s["ping_on_update"] = PingOnUpdateSwitch.IsToggled;
        s["send_hwid"] = SendHwidSwitch.IsToggled;
        s["no_duplicates"] = NoDuplicatesSwitch.IsToggled;
        s["custom_user_agent"] = _customUserAgent;
        s["default_sort"] = _defaultSort;

        _vm.ApplyRuntimeSettings();
        if (sortChanged && _defaultSort == "ping")
            _vm.SortByPing();
        else if (sortChanged && _defaultSort == "name")
            _vm.SortByName();
        else
            _vm.SaveData();
    }

    private async void OnIntervalClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("Интервал автообновления", "Отмена", null,
            "1 час", "3 часа", "6 часов", "12 часов", "24 часа");
        if (action == "1 час") _updateIntervalHours = 1;
        else if (action == "3 часа") _updateIntervalHours = 3;
        else if (action == "6 часов") _updateIntervalHours = 6;
        else if (action == "12 часов") _updateIntervalHours = 12;
        else if (action == "24 часа") _updateIntervalHours = 24;
        else return;

        UpdateIntervalLabel();
    }

    private async void OnUserAgentClicked(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync("User-Agent для подписок", "Отмена", null,
            "v2rayNG/1.9.34",
            "HiddifyNext/2.0.5",
            "Happ/2.0.0",
            "ClashMetaForAndroid/2.11.2",
            "Hivox/2.0 (Android)");
        if (!string.IsNullOrEmpty(action) && action != "Отмена")
        {
            _customUserAgent = action;
            UserAgentLabel.Text = _customUserAgent;
        }
    }

    private void OnSortDefaultClicked(object? sender, EventArgs e)
    {
        _defaultSort = "none";
        UpdateSortCheckmarks();
    }

    private void OnSortPingClicked(object? sender, EventArgs e)
    {
        _defaultSort = "ping";
        UpdateSortCheckmarks();
    }

    private void OnSortNameClicked(object? sender, EventArgs e)
    {
        _defaultSort = "name";
        UpdateSortCheckmarks();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        SaveChanges();
        await Shell.Current.GoToAsync("..");
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        SaveChanges();
        await Shell.Current.GoToAsync("..");
    }
}
