using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Java.Util.Functions;
using Hivox.Mobile.PlatformAndroid;
using Hivox.Mobile.ViewModels;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class ConnectionSettingsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isLoaded;
    private bool _hasSaved;
    private bool _isNavigating;

    public ConnectionSettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hasSaved = false;
        _isNavigating = false;
        NotifServerSwitch.IsToggled = _vm.GetBool("notif_show_server", true);
        ConnectOnOpenSwitch.IsToggled = _vm.GetBool("connect_on_open", false);
        ConnectOnBootSwitch.IsToggled = _vm.GetBool("connect_on_boot", false);
        AutoBestSwitch.IsToggled = _vm.GetBool("auto_select_best", true);
        AutoReconnectSwitch.IsToggled = _vm.GetBool("auto_reconnect", true);
        BypassLanSwitch.IsToggled = _vm.GetBool("bypass_lan", true);
        AllowLanSwitch.IsToggled = _vm.GetBool("allow_lan", false);
        BypassRuSwitch.IsToggled = _vm.GetBool("bypass_ru", true);
        _isLoaded = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SaveChanges();
    }

    private void SaveChanges()
    {
        if (!_isLoaded || _hasSaved) return;
        _hasSaved = true;

        bool routingChanged =
            _vm.GetBool("bypass_lan", true) != BypassLanSwitch.IsToggled ||
            _vm.GetBool("allow_lan", false) != AllowLanSwitch.IsToggled ||
            _vm.GetBool("bypass_ru", true) != BypassRuSwitch.IsToggled;

        _vm.Settings["notif_show_server"] = NotifServerSwitch.IsToggled;
        _vm.Settings["connect_on_open"] = ConnectOnOpenSwitch.IsToggled;
        _vm.Settings["connect_on_boot"] = ConnectOnBootSwitch.IsToggled;
        _vm.Settings["auto_select_best"] = AutoBestSwitch.IsToggled;
        _vm.Settings["auto_reconnect"] = AutoReconnectSwitch.IsToggled;
        _vm.Settings["bypass_lan"] = BypassLanSwitch.IsToggled;
        _vm.Settings["allow_lan"] = AllowLanSwitch.IsToggled;
        _vm.Settings["bypass_ru"] = BypassRuSwitch.IsToggled;
        _vm.ApplyRuntimeSettings();
        _vm.SaveData();

        if (routingChanged && _vm.IsConnected)
            _ = _vm.RestartActiveConnectionAsync();
    }

    private async void OnAddTileTapped(object? sender, TappedEventArgs e)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            await HivoxDialog.ShowAlertAsync("Быстрые настройки", "Плитка быстрых настроек поддерживается начиная с Android 7.0.", "OK");
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var ctx = global::Android.App.Application.Context;
                var statusBar = ctx.GetSystemService(Context.StatusBarService) as StatusBarManager;
                var component = new ComponentName(ctx, Java.Lang.Class.FromType(typeof(HivoxTileService)));
                var icon = global::Android.Graphics.Drawables.Icon.CreateWithResource(ctx, Resource.Drawable.ic_qs_hivox);
                if (statusBar != null && ctx.MainExecutor != null && icon != null)
                {
                    statusBar.RequestAddTileService(
                        component,
                        "Hivox",
                        icon,
                        ctx.MainExecutor,
                        new TileAddConsumer(result =>
                        {
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                string msg = result switch
                                {
                                    1 => "Плитка Hivox уже добавлена в шторку быстрых настроек.",
                                    2 => "Плитка Hivox добавлена в шторку быстрых настроек.",
                                    _ => "Вы можете добавить плитку Hivox вручную через редактирование шторки."
                                };
                                await HivoxDialog.ShowAlertAsync("Шторка быстрых настроек", msg, "OK");
                            });
                        }));
                    return;
                }
            }
            catch { }
        }

        await HivoxDialog.ShowAlertAsync(
            "Добавить плитку Hivox",
            "Откройте шторку быстрых настроек сверху, нажмите значок редактирования (карандаш) и перетащите плитку «Hivox» на панель.",
            "Понятно");
    }

    private sealed class TileAddConsumer : Java.Lang.Object, IConsumer
    {
        private readonly Action<int> _onResult;
        public TileAddConsumer(Action<int> onResult) => _onResult = onResult;

        public void Accept(Java.Lang.Object? t)
        {
            int code = (t as Java.Lang.Number)?.IntValue() ?? -1;
            _onResult(code);
        }
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
