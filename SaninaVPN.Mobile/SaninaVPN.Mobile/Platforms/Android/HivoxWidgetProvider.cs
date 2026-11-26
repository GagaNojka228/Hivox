using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Net;
using Android.Widget;
using Hivox.Mobile.Models;
using Hivox.Mobile.Services;
using Hivox.Mobile.Services.PlatformAndroid;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.PlatformAndroid;

[BroadcastReceiver(
    Name = "org.hivox.mobile.HivoxWidgetProvider",
    Label = "@string/widget_label",
    Exported = false)]
[IntentFilter([
    AppWidgetManager.ActionAppwidgetUpdate
])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/hivox_widget_info")]
public class HivoxWidgetProvider : AppWidgetProvider
{
    public const string ActionWidgetToggle = "org.hivox.mobile.WIDGET_TOGGLE";

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        base.OnUpdate(context, appWidgetManager, appWidgetIds);
        if (context != null)
            UpdateAllWidgets(context);
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        base.OnReceive(context, intent);
        if (context == null || intent == null) return;

        if (intent.Action == ActionWidgetToggle)
        {
            HandleToggle(context);
        }
    }

    private static void HandleToggle(Context context)
    {
        var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
        bool isActive = SaninaVpnService.IsRunning || XrayProcessManager.IsRunning ||
                        vm?.IsConnected == true || vm?.IsConnecting == true;

        if (isActive)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (vm != null)
                    vm.DisconnectFromExternal();
                else
                    SaninaVpnService.StopVpn(context);
                UpdateAllWidgets(context);
            });
            return;
        }

        bool needsUi = vm == null ||
                       (vm.SelectedServer == null && vm.Servers.Count == 0) ||
                       (vm.ConnectionMode == ConnectionMode.Tun && VpnService.Prepare(context) != null);

        if (needsUi)
        {
            var launchIntent = new Intent(context, typeof(MainActivity));
            launchIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            launchIntent.PutExtra("auto_connect", vm != null && vm.Servers.Count > 0);
            context.StartActivity(launchIntent);
            return;
        }

        if (vm!.ConnectionMode == ConnectionMode.Tun)
        {
            SaninaVpnService.StartBootConnect(context);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(async () => await vm.ConnectHeadlessAsync());
        }
        UpdateAllWidgets(context);
    }

    public static void UpdateAllWidgets(Context? context = null)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var ctx = context ?? global::Android.App.Application.Context;
                var mgr = AppWidgetManager.GetInstance(ctx);
                if (mgr == null) return;

                var component = new ComponentName(ctx, Java.Lang.Class.FromType(typeof(HivoxWidgetProvider)));
                int[]? ids = mgr.GetAppWidgetIds(component);
                if (ids == null || ids.Length == 0) return;

                var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
                bool isConnecting = vm?.IsConnecting == true;
                bool isConnected = vm?.IsConnected == true || SaninaVpnService.IsRunning || XrayProcessManager.IsRunning;

                string statusText = isConnecting
                    ? GetSafeString(ctx, Resource.String.qs_tile_connecting, "Подключение...")
                    : isConnected
                        ? (Config.NotifShowServer && vm?.SelectedServer != null
                            ? vm.SelectedServer.Name
                            : GetSafeString(ctx, Resource.String.qs_tile_connected, "Подключено"))
                        : GetSafeString(ctx, Resource.String.qs_tile_disconnected, "Отключено");

                string btnText = (isConnected || isConnecting)
                    ? GetSafeString(ctx, Resource.String.widget_disconnect, "Выкл.")
                    : GetSafeString(ctx, Resource.String.widget_connect, "Включить");

                int btnBgRes = (isConnected || isConnecting)
                    ? Resource.Drawable.widget_btn_active
                    : Resource.Drawable.widget_btn_inactive;

                var pendingFlags = OperatingSystem.IsAndroidVersionAtLeast(23)
                    ? PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent
                    : PendingIntentFlags.UpdateCurrent;

                var openIntent = new Intent(ctx, typeof(MainActivity));
                openIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
                var openPending = PendingIntent.GetActivity(ctx, 201, openIntent, pendingFlags);

                var toggleIntent = new Intent(ctx, typeof(HivoxWidgetProvider));
                toggleIntent.SetAction(ActionWidgetToggle);
                var togglePending = PendingIntent.GetBroadcast(ctx, 202, toggleIntent, pendingFlags);

                var views = new RemoteViews(ctx.PackageName, Resource.Layout.widget_hivox);
                views.SetTextViewText(Resource.Id.widget_status, statusText);
                views.SetTextViewText(Resource.Id.widget_toggle_btn, btnText);
                views.SetInt(Resource.Id.widget_toggle_btn, "setBackgroundResource", btnBgRes);
                if (openPending != null)
                    views.SetOnClickPendingIntent(Resource.Id.widget_root, openPending);
                if (togglePending != null)
                    views.SetOnClickPendingIntent(Resource.Id.widget_toggle_btn, togglePending);

                mgr.UpdateAppWidget(ids, views);
            }
            catch { }
        });
    }

    private static string GetSafeString(Context ctx, int resId, string fallback)
    {
        try { return ctx.GetString(resId); }
        catch { return fallback; }
    }
}
