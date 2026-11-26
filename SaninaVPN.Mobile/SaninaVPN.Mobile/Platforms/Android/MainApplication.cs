using Android.App;
using Android.Content;
using Android.Runtime;
using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.PlatformAndroid;

[Application(
    Name = "org.hivox.mobile.Application",
    Label = "Hivox",
    Icon = "@mipmap/appicon",
    RoundIcon = "@mipmap/appicon_round",
    AllowBackup = false,
    ExtractNativeLibs = true,
    SupportsRtl = true,
    NetworkSecurityConfig = "@xml/network_security_config")]
public class MainApplication : MauiApplication
{
    private BroadcastReceiver? _vpnStateReceiver;

    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        try
        {
            _vpnStateReceiver = new VpnStateBroadcastReceiver();
            var filter = new IntentFilter(SaninaVpnService.ActionVpnState);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                RegisterReceiver(_vpnStateReceiver, filter, ReceiverFlags.NotExported);
            else
                RegisterReceiver(_vpnStateReceiver, filter);
        }
        catch (Exception ex)
        {
            Logger.Error($"MainApplication receiver register failed: {ex.Message}");
        }
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private sealed class VpnStateBroadcastReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            bool connected = intent?.GetBooleanExtra("connected", false) ?? false;
            string? reason = intent?.GetStringExtra("reason");
            long sessionGen = intent?.GetLongExtra("session_gen", 0L) ?? 0L;
            Logger.Log($"VPN broadcast: connected={connected}, reason={reason ?? "none"}, gen={sessionGen}");

            if (!connected && reason is "user" or "revoked" or "error" or "establish_failed" or "destroyed" or "screen_lock")
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
                    vm?.OnSystemVpnStopped(reason, sessionGen);
                });
            }
        }
    }
}
