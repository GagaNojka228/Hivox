using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.PlatformAndroid;

[Activity(
    Name = "org.hivox.mobile.MainActivity",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Android.Content.Intent.ActionView],
    Categories = [Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable],
    DataSchemes = ["hivox", "vless", "vmess", "trojan", "ss", "hysteria2", "hy2"])]
public class MainActivity : MauiAppCompatActivity
{
    internal const int VpnRequestCode = 9001;
    private const int NotificationPermissionCode = 9002;
    internal static TaskCompletionSource<bool>? VpnPermissionTask;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        RequestNotificationPermissionIfNeeded();
        HandleIntentUri(Intent);
        HandleAutoConnectIntent(Intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        try
        {
            var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
            vm?.SyncWithSystemState();
            vm?.ApplyRuntimeSettings();
        }
        catch { }
    }

    private void RequestNotificationPermissionIfNeeded()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.PostNotifications }, NotificationPermissionCode);
            }
        }
    }

    protected override void OnStop()
    {
        base.OnStop();
        try
        {
            if (!IsChangingConfigurations && !SaninaVpnService.IsRunning)
            {
                var pm = GetSystemService(PowerService) as PowerManager;
                bool isScreenOn = pm?.IsInteractive ?? true;
                if (!isScreenOn)
                {
                    var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
                    vm?.OnScreenLockedDisconnect();
                }
            }
        }
        catch { }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleIntentUri(intent);
        HandleAutoConnectIntent(intent);
    }

    private static void HandleAutoConnectIntent(Intent? intent)
    {
        if (intent == null || !intent.GetBooleanExtra("auto_connect", false)) return;
        intent.RemoveExtra("auto_connect");

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
                if (vm != null && vm.IsDisconnected && vm.Servers.Count > 0)
                    await vm.ToggleConnection();
            }
            catch (Exception ex)
            {
                Logger.Error($"Auto-connect intent error: {ex.Message}");
            }
        });
    }

    private static void HandleIntentUri(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data == null) return;
        string? rawUri = intent.Data.ToString();
        if (string.IsNullOrWhiteSpace(rawUri)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
                if (vm == null) return;

                if (rawUri.StartsWith("hivox://", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = global::Android.Net.Uri.Parse(rawUri);
                    string? paramUrl = parsed?.GetQueryParameter("url");
                    string target = !string.IsNullOrWhiteSpace(paramUrl)
                        ? paramUrl
                        : rawUri[8..].TrimStart('/');

                    if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        await vm.AddSubscription(target);
                    else if (!string.IsNullOrWhiteSpace(target))
                        await vm.ImportMassTextAsync(target);
                }
                else
                {
                    await vm.AddServerUri(rawUri);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Deep link handling error: {ex.Message}");
            }
        });
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VpnRequestCode)
            VpnPermissionTask?.TrySetResult(resultCode == Result.Ok);
    }
}
