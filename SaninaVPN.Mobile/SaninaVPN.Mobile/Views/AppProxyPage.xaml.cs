using System.Collections.ObjectModel;
using Android.Content.PM;
using Hivox.Mobile.Models;
using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class AppProxyPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly List<AppProxyItem> _allApps = new();
    private HashSet<string> _selectedPackages = new(StringComparer.OrdinalIgnoreCase);
    private string _mode = "bypass";
    private bool _isLoading = true;
    private bool _isInitialized;
    private bool _suppressEvents;
    private bool _dirty;
    private bool _didRestart;
    private bool _isNavigating;

    public AppProxyPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        Loaded += async (_, _) => await InitializePageAsync();
    }

    private async Task InitializePageAsync()
    {
        _suppressEvents = true;
        MasterSwitch.IsToggled = _vm.GetBool("per_app_proxy_enabled", false);
        _mode = _vm.GetString("per_app_proxy_mode", "bypass");
        string csv = _vm.GetString("per_app_packages", "");
        _selectedPackages = new HashSet<string>(
            csv.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim())
               .Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _suppressEvents = false;
        _isInitialized = true;

        UpdateModeCardsVisual();
        UpdateSelectedCount();

        if (_allApps.Count == 0)
            await LoadInstalledAppsAsync();
    }

    private async Task LoadInstalledAppsAsync()
    {
        _isLoading = true;
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        EmptyAppsLabel.Text = "Загрузка установленных приложений...";

        var loaded = await Task.Run(() =>
        {
            var list = new List<AppProxyItem>();
            try
            {
                var ctx = global::Android.App.Application.Context;
                var pm = ctx.PackageManager;
                if (pm == null) return list;

                string selfPkg = ctx.PackageName ?? "org.hivox.mobile";
                var apps = pm.GetInstalledApplications(PackageInfoFlags.MetaData);
                if (apps == null) return list;

                foreach (var app in apps)
                {
                    string pkg = app.PackageName ?? "";
                    if (string.IsNullOrWhiteSpace(pkg) || string.Equals(pkg, selfPkg, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isSys = (app.Flags & ApplicationInfoFlags.System) != 0 &&
                                 (app.Flags & ApplicationInfoFlags.UpdatedSystemApp) == 0;
                    string label;
                    try
                    {
                        label = app.LoadLabel(pm)?.Trim() ?? pkg;
                    }
                    catch
                    {
                        label = pkg;
                    }
                    if (string.IsNullOrWhiteSpace(label))
                        label = pkg;

                    list.Add(new AppProxyItem
                    {
                        PackageName = pkg,
                        AppName = label,
                        IsSystemApp = isSys,
                        IsSelected = _selectedPackages.Contains(pkg)
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadInstalledAppsAsync failed: {ex.Message}");
            }
            return list;
        });

        _allApps.Clear();
        _allApps.AddRange(loaded);
        _isLoading = false;
        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
        EmptyAppsLabel.Text = "Приложения не найдены";

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = (SearchEntry.Text ?? "").Trim();
        bool hideSys = HideSystemSwitch.IsToggled;

        var query = _allApps.Where(a =>
            (!hideSys || !a.IsSystemApp || a.IsSelected) &&
            (string.IsNullOrEmpty(q) ||
             a.AppName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
             a.PackageName.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.IsSelected)
            .ThenBy(a => a.AppName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AppsCollectionView.ItemsSource = new ObservableCollection<AppProxyItem>(query);
        UpdateSelectedCount();
    }

    private void UpdateModeCardsVisual()
    {
        Color Resolve(string key, string fallback) =>
            Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Color.FromArgb(fallback);

        var primary = Resolve("M3Primary", "#7C4DFF");
        var secondary = Resolve("M3Secondary", "#B388FF");
        var primaryContainer = Resolve("M3PrimaryContainer", "#2A1B3D");
        var surfaceContainer = Resolve("M3SurfaceContainer", "#201533");
        var outlineVariant = Resolve("M3OutlineVariant", "#3A2856");
        var onSurface = Resolve("M3OnSurface", "#E6E1F5");

        bool isBypass = _mode != "only_vpn";

        BypassCard.Stroke = new SolidColorBrush(isBypass ? primary : outlineVariant);
        BypassCard.BackgroundColor = isBypass ? primaryContainer : surfaceContainer;
        BypassIcon.TextColor = isBypass ? secondary : onSurface;
        BypassTitle.TextColor = isBypass ? secondary : onSurface;

        TunnelOnlyCard.Stroke = new SolidColorBrush(!isBypass ? primary : outlineVariant);
        TunnelOnlyCard.BackgroundColor = !isBypass ? primaryContainer : surfaceContainer;
        TunnelOnlyIcon.TextColor = !isBypass ? secondary : onSurface;
        TunnelOnlyTitle.TextColor = !isBypass ? secondary : onSurface;
    }

    private void UpdateSelectedCount()
    {
        SelectedCountLabel.Text = $"Выбрано приложений: {_selectedPackages.Count}";
    }

    private void SaveState(bool markDirty = true)
    {
        if (_suppressEvents || !_isInitialized) return;
        if (markDirty) _dirty = true;
        _vm.Settings["per_app_proxy_enabled"] = MasterSwitch.IsToggled;
        _vm.Settings["per_app_proxy_mode"] = _mode;
        _vm.Settings["per_app_packages"] = string.Join(",", _selectedPackages.OrderBy(s => s));
        _vm.ApplyRuntimeSettings();
        _vm.SaveData();
    }

    private void OnMasterToggled(object? sender, ToggledEventArgs e) => SaveState();

    private void OnBypassModeTapped(object? sender, TappedEventArgs e)
    {
        if (_mode == "bypass") return;
        _mode = "bypass";
        UpdateModeCardsVisual();
        SaveState();
    }

    private void OnTunnelOnlyModeTapped(object? sender, TappedEventArgs e)
    {
        if (_mode == "only_vpn") return;
        _mode = "only_vpn";
        UpdateModeCardsVisual();
        SaveState();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isLoading) ApplyFilter();
    }

    private void OnHideSystemToggled(object? sender, ToggledEventArgs e)
    {
        if (!_isLoading) ApplyFilter();
    }

    private void OnAppRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is AppProxyItem item)
        {
            item.IsSelected = !item.IsSelected;
            SyncItemSelection(item);
        }
    }

    private void SyncItemSelection(AppProxyItem item)
    {
        if (item.IsSelected)
            _selectedPackages.Add(item.PackageName);
        else
            _selectedPackages.Remove(item.PackageName);

        UpdateSelectedCount();
        SaveState();
    }

    private void OnClearAllTapped(object? sender, TappedEventArgs e)
    {
        if (_selectedPackages.Count == 0) return;
        _selectedPackages.Clear();
        foreach (var app in _allApps)
            app.IsSelected = false;
        ApplyFilter();
        SaveState();
    }

    private async void OnImportAppsTapped(object? sender, TappedEventArgs e)
    {
        string? clip = await Clipboard.Default.GetTextAsync();
        if (string.IsNullOrWhiteSpace(clip))
        {
            await HivoxDialog.ShowAlertAsync("Буфер пуст", "Скопируйте список пакетов приложений (через запятую или с новой строки).", "OK");
            return;
        }

        int added = 0;
        foreach (var token in clip.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string pkg = token.Trim();
            if (pkg.Contains('.') && _selectedPackages.Add(pkg))
                added++;
        }

        if (added > 0)
        {
            foreach (var app in _allApps)
                app.IsSelected = _selectedPackages.Contains(app.PackageName);

            ApplyFilter();
            SaveState();
        }
        await HivoxDialog.ShowAlertAsync("Импорт завершён", $"Добавлено приложений: {added} (всего выбрано: {_selectedPackages.Count}).", "OK");
    }

    private async void OnExportAppsTapped(object? sender, TappedEventArgs e)
    {
        if (_selectedPackages.Count == 0)
        {
            await HivoxDialog.ShowAlertAsync("Пусто", "Нет выбранных приложений для экспорта.", "OK");
            return;
        }
        string text = string.Join("\n", _selectedPackages.OrderBy(x => x));
        await Clipboard.Default.SetTextAsync(text);
        await HivoxDialog.ShowAlertAsync("Скопировано", $"Список из {_selectedPackages.Count} приложений скопирован в буфер обмена.", "OK");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ApplyAndReconnectIfNeeded();
    }

    private void ApplyAndReconnectIfNeeded()
    {
        if (!_isInitialized || !_dirty) return;
        _dirty = false;
        SaveState(markDirty: false);
        if (!_didRestart && _vm.IsConnected && _vm.IsTunMode)
        {
            _didRestart = true;
            _ = _vm.RestartActiveConnectionAsync();
        }
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        ApplyAndReconnectIfNeeded();
        await Shell.Current.GoToAsync("..");
    }

    private async void OnDoneTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        ApplyAndReconnectIfNeeded();
        await Shell.Current.GoToAsync("..");
    }
}
