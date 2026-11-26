using Android.App;
using Android.Content;
using Android.Net;
using Hivox.Mobile.Services;

namespace Hivox.Mobile.PlatformAndroid;

[BroadcastReceiver(
    Name = "org.hivox.mobile.BootReceiver",
    Enabled = true,
    Exported = true,
    DirectBootAware = false)]
[IntentFilter([
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced,
    "android.intent.action.QUICKBOOT_POWERON"
])]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        string? action = intent.Action;
        if (action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced or "android.intent.action.QUICKBOOT_POWERON"))
            return;

        try
        {
            var prefs = context.GetSharedPreferences("sanina_vpn", FileCreationMode.Private);
            bool connectOnBoot = prefs?.GetBoolean("connect_on_boot", false) ?? false;
            if (!connectOnBoot) return;

            if (SaninaVpnService.IsRunning) return;

            if (VpnService.Prepare(context) != null)
            {
                Logger.Log($"BootReceiver ({action}): VPN permission not granted yet, skipping auto-start");
                return;
            }

            Logger.Log($"BootReceiver ({action}): starting Hivox VPN service synchronously");
            SaninaVpnService.StartBootConnect(context);
        }
        catch (Exception ex)
        {
            Logger.Error($"BootReceiver error: {ex.Message}");
        }
    }
}
