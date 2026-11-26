using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Net;
using Android.Service.QuickSettings;
using Hivox.Mobile.Models;
using Hivox.Mobile.Services;
using Hivox.Mobile.Services.PlatformAndroid;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.PlatformAndroid;

[SupportedOSPlatform("android24.0")]
[Service(
    Name = "org.hivox.mobile.HivoxTileService",
    Exported = true,
    Icon = "@drawable/ic_qs_hivox",
    Label = "@string/qs_tile_label",
    Permission = "android.permission.BIND_QUICK_SETTINGS_TILE")]
[IntentFilter(["android.service.quicksettings.action.QS_TILE"])]
[MetaData("android.service.quicksettings.ACTIVE_TILE", Value = "true")]
[MetaData("android.service.quicksettings.TOGGLEABLE_TILE", Value = "true")]
public class HivoxTileService : TileService
{
    public override void OnTileAdded()
    {
        base.OnTileAdded();
        RefreshTile();
    }

    public override void OnStartListening()
    {
        base.OnStartListening();
        RefreshTile();
    }

    public override void OnClick()
    {
        base.OnClick();
        var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
        bool isActive = SaninaVpnService.IsRunning || XrayProcessManager.IsRunning ||
                        vm?.IsConnected == true || vm?.IsConnecting == true;

        if (isActive)
        {
            UpdateTileVisual(TileState.Inactive, GetSafeString(Resource.String.qs_tile_disconnected, "Отключено"));
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (vm != null)
                    vm.DisconnectFromExternal();
                else
                    SaninaVpnService.StopVpn(this);
            });
            return;
        }

        bool needsUi = vm == null ||
                       (vm.SelectedServer == null && vm.Servers.Count == 0) ||
                       (vm.ConnectionMode == ConnectionMode.Tun && VpnService.Prepare(this) != null);

        if (needsUi)
        {
            var launchIntent = new Intent(this, typeof(MainActivity));
            launchIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            launchIntent.PutExtra("auto_connect", vm != null && vm.Servers.Count > 0);

            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                var pending = PendingIntent.GetActivity(
                    this,
                    102,
                    launchIntent,
                    PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
                if (pending != null)
                    StartActivityAndCollapse(pending);
            }
            else
            {
#pragma warning disable CA1422
                StartActivityAndCollapse(launchIntent);
#pragma warning restore CA1422
            }
            return;
        }

        UpdateTileVisual(TileState.Active, GetSafeString(Resource.String.qs_tile_connecting, "Подключение..."));
        if (vm!.ConnectionMode == ConnectionMode.Tun)
        {
            SaninaVpnService.StartBootConnect(this);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(async () => await vm.ConnectHeadlessAsync());
        }
    }

    private void RefreshTile()
    {
        var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
        bool isConnecting = vm?.IsConnecting == true;
        bool isConnected = vm?.IsConnected == true || SaninaVpnService.IsRunning || XrayProcessManager.IsRunning;

        TileState state = (isConnected || isConnecting) ? TileState.Active : TileState.Inactive;
        string subtitle = isConnecting
            ? GetSafeString(Resource.String.qs_tile_connecting, "Подключение...")
            : isConnected
                ? (Config.NotifShowServer && vm?.SelectedServer != null
                    ? vm.SelectedServer.Name
                    : GetSafeString(Resource.String.qs_tile_connected, "Подключено"))
                : GetSafeString(Resource.String.qs_tile_disconnected, "Отключено");

        UpdateTileVisual(state, subtitle);
    }

    private void UpdateTileVisual(TileState state, string subtitle)
    {
        try
        {
            var tile = QsTile;
            if (tile == null) return;

            tile.State = state;
            tile.Label = GetSafeString(Resource.String.qs_tile_label, "Hivox");
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                tile.Subtitle = subtitle;
            tile.UpdateTile();
        }
        catch { }
    }

    private string GetSafeString(int resId, string fallback)
    {
        try { return GetString(resId); }
        catch { return fallback; }
    }
}

public static class HivoxTileHelper
{
    public static void RequestTileUpdate(Context? context = null)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(24)) return;
        _ = Task.Run(() =>
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(24)) return;
            try
            {
                var ctx = context ?? global::Android.App.Application.Context;
                var component = new ComponentName(ctx, Java.Lang.Class.FromType(typeof(HivoxTileService)));
                TileService.RequestListeningState(ctx, component);
            }
            catch { }
        });
    }
}
