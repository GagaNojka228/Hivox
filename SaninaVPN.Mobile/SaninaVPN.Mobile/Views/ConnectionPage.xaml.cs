using Hivox.Mobile.Models;
using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class ConnectionPage : ContentPage
{
    private readonly MainViewModel _vm;
    public MainViewModel ViewModel => _vm;
    private bool _initialized;
    private bool _isOpeningGroupMenu;

    public ConnectionPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        _vm.SpeedUpdated += (up, down) => MainThread.BeginInvokeOnMainThread(() => TrafficGraph.AddDataPoint(up, down));
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsConnected) && !_vm.IsConnected)
                MainThread.BeginInvokeOnMainThread(() => TrafficGraph.Clear());
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        TrafficGraph.Invalidate();
        if (!_initialized)
        {
            _initialized = true;
            await _vm.InitializeAsync();
        }
    }

    private async void OnPowerButtonTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await PowerButtonBorder.ScaleToAsync(0.93, 70, Easing.CubicOut);
            await PowerButtonBorder.ScaleToAsync(1.0, 90, Easing.CubicIn);
            if (_vm.ToggleConnectionCommand.CanExecute(null))
                _vm.ToggleConnectionCommand.Execute(null);
        }
        catch (Exception ex)
        {
            Logger.Error($"PowerButton tap error: {ex.Message}");
        }
    }

    private async void OnSelectedNodeTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.Servers.Count == 0)
        {
            await ShowAddActionSheetAsync();
            return;
        }

        if (_vm.ActiveSubscriptionGroup != null && !_vm.ActiveSubscriptionGroup.IsExpanded)
        {
            _vm.ActiveSubscriptionGroup.IsExpanded = true;
            await LowerScrollView.ScrollToAsync(0, 0, true);
        }
        else
        {
            await Shell.Current.GoToAsync("//servers");
        }
    }

    private async void OnSwitchSubscriptionTapped(object? sender, TappedEventArgs e)
    {
        await ShowSubscriptionSwitcherAsync();
    }

    private async Task ShowSubscriptionSwitcherAsync()
    {
        if (_vm.SubscriptionGroups.Count <= 1) return;

        var names = _vm.SubscriptionGroups
            .Select(g => g == _vm.ActiveSubscriptionGroup
                ? $"✓ {g.DisplayName} ({g.Servers.Count})"
                : $"{g.DisplayName} ({g.Servers.Count})")
            .ToArray();

        string? choice = await HivoxDialog.ShowActionSheetAsync("Активная подписка", "Отмена", null, names);
        if (string.IsNullOrEmpty(choice) || choice == "Отмена") return;

        int idx = Array.IndexOf(names, choice);
        if (idx >= 0 && idx < _vm.SubscriptionGroups.Count)
            await _vm.SwitchActiveSubscriptionAsync(_vm.SubscriptionGroups[idx]);
    }

    private void OnToggleGroupTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SubscriptionGroup group)
            _vm.ToggleGroupCommand.Execute(group);
    }

    private static async Task AnimateTapAsync(object? sender, bool spinIcon = false)
    {
        if (sender is not VisualElement ve) return;
        try
        {
            if (spinIcon && ve is Border { Content: VisualElement icon })
                _ = icon.RotateToAsync(360, 420, Easing.CubicInOut).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => icon.Rotation = 0));

            await Task.WhenAll(
                ve.ScaleToAsync(0.84, 70, Easing.CubicOut),
                ve.FadeToAsync(0.65, 70, Easing.CubicOut));
            await Task.WhenAll(
                ve.ScaleToAsync(1.0, 130, Easing.SpringOut),
                ve.FadeToAsync(1.0, 120, Easing.CubicIn));
        }
        catch { }
    }

    private async void OnUpdateGroupTapped(object? sender, TappedEventArgs e)
    {
        _ = AnimateTapAsync(sender, spinIcon: true);
        await Task.Yield();
        if ((sender as BindableObject)?.BindingContext is SubscriptionGroup group)
            _vm.UpdateGroupCommand.Execute(group);
    }

    private void OnPingGroupTapped(object? sender, TappedEventArgs e)
    {
        _ = AnimateTapAsync(sender);
        if ((sender as BindableObject)?.BindingContext is SubscriptionGroup group)
            _vm.PingGroupCommand.Execute(group);
    }

    private async void OnGroupMoreTapped(object? sender, TappedEventArgs e)
    {
        if (_isOpeningGroupMenu) return;
        _isOpeningGroupMenu = true;
        try
        {
            await AnimateTapAsync(sender);
            if ((sender as BindableObject)?.BindingContext is not SubscriptionGroup group) return;

            var items = new List<(string Icon, string Text, bool IsDestructive)>
            {
                ("\uE8B8", "Настройки", false),
                ("\uE80D", "Поделиться", false)
            };
            if (group.HasUrl)
                items.Add(("\uE872", "Удалить", true));

            string? action = await HivoxDialog.ShowIconMenuAsync(group.DisplayName, items.ToArray());
            switch (action)
            {
                case "Настройки":
                    _vm.EditingSubscriptionUrl = group.Url;
                    await Shell.Current.GoToAsync("providersettings");
                    break;
                case "Поделиться":
                {
                    string shareText = group.HasUrl
                        ? group.Url
                        : (_vm.SelectedServer?.Uri ?? "");
                    if (string.IsNullOrWhiteSpace(shareText))
                    {
                        await _vm.ShowAlert("Поделиться", "Нет ссылки для отправки.");
                        return;
                    }
                    try
                    {
                        await Share.Default.RequestAsync(new ShareTextRequest
                        {
                            Title = group.DisplayName,
                            Text = shareText
                        });
                    }
                    catch
                    {
                        await Clipboard.Default.SetTextAsync(shareText);
                        await _vm.ShowAlert("Скопировано", "Ссылка скопирована в буфер обмена.");
                    }
                    break;
                }
                case "Удалить":
                    if (group.HasUrl)
                        _vm.DeleteSubscriptionCommand.Execute(group.Url);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"OnGroupMoreTapped error: {ex.Message}");
        }
        finally
        {
            _isOpeningGroupMenu = false;
        }
    }

    private async void OnSupportTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SubscriptionGroup group && group.HasSupportUrl)
            await OpenExternalUrlAsync(group.SupportUrl);
    }

    private async void OnWebPageTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SubscriptionGroup group && group.HasWebPageUrl)
            await OpenExternalUrlAsync(group.WebPageUrl);
    }

    private async Task OpenExternalUrlAsync(string rawUrl)
    {
        try
        {
            if (Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                await Launcher.Default.OpenAsync(uri);
            }
        }
        catch (Exception ex)
        {
            await _vm.ShowAlert("Ошибка", $"Не удалось открыть ссылку: {ex.Message}");
        }
    }

    private void OnServerTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ServerInfo server)
            _vm.ConnectToServerCommand.Execute(server);
    }

    private async void OnImportTapped(object? sender, TappedEventArgs e)
    {
        await ShowAddActionSheetAsync();
    }

    private async void OnPasteClipboardTapped(object? sender, TappedEventArgs e)
    {
        string? clip = await Clipboard.Default.GetTextAsync();
        if (string.IsNullOrWhiteSpace(clip))
        {
            await _vm.ShowAlert("Буфер пуст", "Скопируйте URL подписки или ссылку сервера в буфер обмена.");
            return;
        }
        await _vm.ImportMassTextAsync(clip);
    }

    private async void OnSubscriptionsShortcutTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("subscriptions");
    }

    private async void OnConnectionCheckTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("conncheck");
    }

    private async void OnQrScanTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("qrscanner");
    }

    private async Task ShowAddActionSheetAsync()
    {
        string? action = await HivoxDialog.ShowActionSheetAsync(
            "Добавить в Hivox", "Отмена", null,
            "Подписка по URL",
            "Ссылка сервера (vless://, vmess://...)",
            "Вставить из буфера обмена",
            "Сканировать QR-код / из галереи");

        switch (action)
        {
            case "Подписка по URL":
            {
                string? url = await HivoxDialog.ShowPromptAsync(
                    "Добавить подписку",
                    "Введите URL подписки:",
                    "Добавить", "Отмена",
                    placeholder: "https://example.com/sub",
                    keyboard: Keyboard.Url);
                if (!string.IsNullOrWhiteSpace(url))
                    await _vm.AddSubscription(url);
                break;
            }
            case "Ссылка сервера (vless://, vmess://...)":
            {
                string? uri = await HivoxDialog.ShowPromptAsync(
                    "Добавить сервер",
                    "Вставьте URI сервера:",
                    "Добавить", "Отмена",
                    placeholder: "vless://...");
                if (!string.IsNullOrWhiteSpace(uri))
                    await _vm.AddServerUri(uri);
                break;
            }
            case "Вставить из буфера обмена":
            {
                string? clip = await Clipboard.Default.GetTextAsync();
                if (string.IsNullOrWhiteSpace(clip))
                {
                    await _vm.ShowAlert("Буфер пуст", "Скопируйте ссылки серверов или URL подписки.");
                    return;
                }
                await _vm.ImportMassTextAsync(clip);
                break;
            }
            case "Сканировать QR-код / из галереи":
                await Shell.Current.GoToAsync("qrscanner");
                break;
        }
    }
}
