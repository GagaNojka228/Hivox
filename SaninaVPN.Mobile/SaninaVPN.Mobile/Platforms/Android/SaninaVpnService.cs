using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Hivox.Mobile.Services;
using Hivox.Mobile.Services.PlatformAndroid;
using Hivox.Mobile.ViewModels;
using System.Net;
using System.Runtime.InteropServices;

namespace Hivox.Mobile.PlatformAndroid;

[Service(
    Name = "org.hivox.mobile.VpnService",
    ForegroundServiceType = Android.Content.PM.ForegroundService.TypeSpecialUse,
    Exported = true,
    Permission = "android.permission.BIND_VPN_SERVICE")]
[IntentFilter(["android.net.VpnService"])]
[Property("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE", Value = "vpn_tunnel")]
public class SaninaVpnService : VpnService
{
    private const int NotificationId = 1001;
    private const string ChannelId = "hivox_vpn_channel";
    private const string ActionStop = "org.hivox.mobile.STOP_VPN";
    public const string ActionBootConnect = "org.hivox.mobile.BOOT_CONNECT";
    public const string ActionVpnState = "org.hivox.mobile.VPN_STATE";

    private const int FGetFd = 1;
    private const int FSetFd = 2;
    private const int FdCloexec = 1;

    [DllImport("c", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);

    public static TaskCompletionSource<int>? TunFdReady;
    public static volatile bool IsRunning;
    private static SaninaVpnService? _activeInstance;
    private static long _requestedSessionGen;
    private static long _cancelledUpToGen;

    public static long CurrentRequestedSessionGen => Volatile.Read(ref _requestedSessionGen);

    private ParcelFileDescriptor? _vpnInterface;
    private PowerManager.WakeLock? _wakeLock;
    private BroadcastReceiver? _screenOffReceiver;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<int>? _sessionTunTcs;
    private long _sessionGen;
    private volatile bool _cleanedUp;
    private string _serverLabel = "Подключено";
    private bool _enableIpv6;

    public override void OnCreate()
    {
        base.OnCreate();
        _activeInstance = this;
        CreateNotificationChannel();

        try
        {
            _screenOffReceiver = new ScreenOffReceiver(this);
            var filter = new IntentFilter(Intent.ActionScreenOff);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                RegisterReceiver(_screenOffReceiver, filter, ReceiverFlags.NotExported);
            else
                RegisterReceiver(_screenOffReceiver, filter);
        }
        catch { }
    }

    private void StartForegroundWithType(Notification notification)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, notification, Android.Content.PM.ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, notification);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            bool fromUi = intent.GetBooleanExtra("from_ui", false);
            long stopGen = intent.GetLongExtra("session_gen", _sessionGen);
            Logger.Log($"VPN service received ActionStop (fromUi={fromUi}, gen={stopGen})");
            if (stopGen == 0 || stopGen >= _sessionGen)
                StopFromService(fromUi ? "ui" : "user", broadcastStop: !fromUi);
            return StartCommandResult.NotSticky;
        }

        if (intent?.Action == ActionBootConnect)
        {
            long bootGen = intent.GetLongExtra("session_gen", CurrentRequestedSessionGen);
            if (bootGen == 0)
                bootGen = Interlocked.Increment(ref _requestedSessionGen);

            _cleanedUp = false;
            _activeInstance = this;
            _sessionGen = bootGen;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _serverLabel = GetSafeString(Resource.String.qs_tile_connecting, "Подключение...");
            IsRunning = true;

            try
            {
                StartForegroundWithType(BuildNotification(_serverLabel));
            }
            catch (Exception ex)
            {
                Logger.Error($"Boot StartForeground failed: {ex.Message}");
                StopFromService("boot_failed", broadcastStop: false);
                return StartCommandResult.NotSticky;
            }

            HivoxTileHelper.RequestTileUpdate(this);
            HivoxWidgetProvider.UpdateAllWidgets(this);

            var bootToken = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    var vm = IPlatformApplication.Current?.Services?.GetService<MainViewModel>();
                    if (vm == null)
                    {
                        Logger.Error("Headless boot/tile connect: MainViewModel unavailable");
                        StopFromService("boot_failed", broadcastStop: false);
                        return;
                    }

                    bool ok = await vm.ConnectFromBootServiceAsync(this, bootToken);
                    if (!ok && !bootToken.IsCancellationRequested)
                        StopFromService("boot_failed", broadcastStop: false);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Headless boot/tile connect exception: {ex.Message}");
                    StopFromService("boot_failed", broadcastStop: false);
                }
            });

            return StartCommandResult.Sticky;
        }

        long gen = intent?.GetLongExtra("session_gen", CurrentRequestedSessionGen) ?? CurrentRequestedSessionGen;
        bool alreadyCancelled = gen <= Volatile.Read(ref _cancelledUpToGen);

        _cleanedUp = false;
        _activeInstance = this;
        _sessionGen = gen;
        _sessionTunTcs = TunFdReady;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _serverLabel = intent?.GetStringExtra("server_name") ?? "VPN подключен";
        _enableIpv6 = intent?.GetBooleanExtra("ipv6", false) ?? false;
        IsRunning = !alreadyCancelled;

        if (!alreadyCancelled)
            AcquireWakeLockIfNeeded();

        string notifText = Config.NotifShowServer
            ? _serverLabel
            : GetSafeString(Resource.String.notif_protected_active, "Защищённое соединение активно");
        var notification = BuildNotification(notifText);
        try
        {
            StartForegroundWithType(notification);
        }
        catch (Exception ex)
        {
            Logger.Error($"StartForeground failed: {ex.Message}");
            _sessionTunTcs?.TrySetException(ex);
            StopFromService("error", broadcastStop: true);
            return StartCommandResult.NotSticky;
        }

        if (alreadyCancelled)
        {
            Logger.Log($"VPN start cancelled before SetupTun (gen={gen})");
            StopFromService("ui", broadcastStop: false);
            return StartCommandResult.NotSticky;
        }

        var token = _cts.Token;
        var sessionTcs = _sessionTunTcs;
        Task.Run(() => SetupTun(token, sessionTcs));

        return StartCommandResult.Sticky;
    }

    internal async Task<int> EstablishTunForBootAsync(string serverLabel, bool enableIpv6, CancellationToken token)
    {
        _serverLabel = serverLabel;
        _enableIpv6 = enableIpv6;
        AcquireWakeLockIfNeeded();
        RefreshRuntimeNotification();

        var bootTunTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessionTunTcs = bootTunTcs;
        TunFdReady = bootTunTcs;
        _ = Task.Run(() => SetupTun(token, bootTunTcs));
        return await bootTunTcs.Task;
    }

    public override void OnDestroy()
    {
        try
        {
            if (_screenOffReceiver != null)
            {
                UnregisterReceiver(_screenOffReceiver);
                _screenOffReceiver = null;
            }
        }
        catch { }
        DoCleanup(broadcastStop: IsRunning, reason: "destroyed");
        base.OnDestroy();
    }

    public override void OnRevoke()
    {
        Logger.Log("VPN permission revoked by system");
        DoCleanup(broadcastStop: true, reason: "revoked");
        base.OnRevoke();
    }

    private void StopFromService(string reason, bool broadcastStop = true)
    {
        DoCleanup(broadcastStop: broadcastStop, reason: reason);
        StopSelf();
    }

    private void DoCleanup(bool broadcastStop, string reason)
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        IsRunning = false;

        _cts?.Cancel();
        _sessionTunTcs?.TrySetCanceled();
        if (ReferenceEquals(TunFdReady, _sessionTunTcs))
            TunFdReady?.TrySetCanceled();

        XrayProcessManager.Stop();

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                StopForeground(StopForegroundFlags.Remove);
            else
#pragma warning disable CA1422
                StopForeground(true);
#pragma warning restore CA1422
        }
        catch { }

        try { _vpnInterface?.Close(); } catch { }
        _vpnInterface = null;

        ReleaseWakeLock();
        if (_activeInstance == this)
            _activeInstance = null;

        SaveVpnState(false);
        HivoxTileHelper.RequestTileUpdate(this);
        HivoxWidgetProvider.UpdateAllWidgets(this);

        if (broadcastStop)
        {
            try
            {
                var intent = new Intent(ActionVpnState);
                intent.SetPackage(PackageName);
                intent.PutExtra("connected", false);
                intent.PutExtra("reason", reason);
                intent.PutExtra("session_gen", _sessionGen);
                SendBroadcast(intent);
            }
            catch { }
        }
    }

    private static (string Ip, int Prefix) ParseCidr(string cidr, int defaultPrefix)
    {
        var parts = cidr.Split('/', 2);
        string ip = parts[0].Trim();
        int prefix = parts.Length == 2 && int.TryParse(parts[1].Trim(), out int p) ? p : defaultPrefix;
        return (ip, prefix);
    }

    private void SetupTun(CancellationToken token, TaskCompletionSource<int>? sessionTcs)
    {
        try
        {
            token.ThrowIfCancellationRequested();

            var builder = new Builder(this);
            builder.SetSession("Hivox");
            builder.SetMtu(Config.TunMtu);
            var (v4Ip, v4Prefix) = ParseCidr(Config.TunInet4Address, 30);
            builder.AddAddress(v4Ip, v4Prefix);
            builder.AddRoute("0.0.0.0", 0);

            if (_enableIpv6)
            {
                var (v6Ip, v6Prefix) = ParseCidr(Config.TunInet6Address, 126);
                builder.AddAddress(v6Ip, v6Prefix);
                builder.AddRoute("::", 0);
            }

            string rawDns = string.IsNullOrWhiteSpace(Config.CustomDns) ? "1.1.1.1" : Config.CustomDns.Trim();
            string primaryDns = IPAddress.TryParse(rawDns, out _) ? rawDns : "1.1.1.1";
            builder.AddDnsServer(primaryDns);
            if (primaryDns != "8.8.8.8")
                builder.AddDnsServer("8.8.8.8");
            builder.SetBlocking(false);

            string selfPackage = PackageName ?? "org.hivox.mobile";
            if (Config.PerAppProxyEnabled && Config.PerAppPackages.Count > 0)
            {
                if (string.Equals(Config.PerAppProxyMode, "only_vpn", StringComparison.OrdinalIgnoreCase))
                {
                    int allowedCount = 0;
                    foreach (var pkg in Config.PerAppPackages)
                    {
                        if (string.IsNullOrWhiteSpace(pkg) || string.Equals(pkg, selfPackage, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try
                        {
                            builder.AddAllowedApplication(pkg.Trim());
                            allowedCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"AddAllowedApplication({pkg}) skipped: {ex.Message}");
                        }
                    }
                    if (allowedCount == 0)
                        builder.AddDisallowedApplication(selfPackage);
                    else
                        Logger.Log($"Per-app VPN (only_vpn): {allowedCount} apps allowed");
                }
                else
                {
                    builder.AddDisallowedApplication(selfPackage);
                    int bypassedCount = 0;
                    foreach (var pkg in Config.PerAppPackages)
                    {
                        if (string.IsNullOrWhiteSpace(pkg) || string.Equals(pkg, selfPackage, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try
                        {
                            builder.AddDisallowedApplication(pkg.Trim());
                            bypassedCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"AddDisallowedApplication({pkg}) skipped: {ex.Message}");
                        }
                    }
                    Logger.Log($"Per-app VPN (bypass): {bypassedCount} apps bypassed");
                }
            }
            else
            {
                builder.AddDisallowedApplication(selfPackage);
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                builder.SetMetered(false);

            if (OperatingSystem.IsAndroidVersionAtLeast(23))
                builder.SetUnderlyingNetworks(null);

            token.ThrowIfCancellationRequested();

            _vpnInterface = builder.Establish();
            if (_vpnInterface == null)
            {
                var err = new Exception("Не удалось создать TUN-интерфейс (VpnService.Establish вернул null)");
                sessionTcs?.TrySetException(err);
                DoCleanup(broadcastStop: true, reason: "establish_failed");
                return;
            }

            if (token.IsCancellationRequested)
            {
                try { _vpnInterface.Close(); } catch { }
                _vpnInterface = null;
                sessionTcs?.TrySetCanceled();
                return;
            }

            int tunFd = _vpnInterface.Fd;
            if (tunFd >= 0)
                ClearCloseOnExec(tunFd);

            Logger.Log($"VPN TUN established, fd={tunFd}");
            SaveVpnState(true);
            HivoxTileHelper.RequestTileUpdate(this);
            HivoxWidgetProvider.UpdateAllWidgets(this);

            try
            {
                var stateIntent = new Intent(ActionVpnState);
                stateIntent.SetPackage(PackageName);
                stateIntent.PutExtra("connected", true);
                stateIntent.PutExtra("session_gen", _sessionGen);
                SendBroadcast(stateIntent);
            }
            catch { }

            sessionTcs?.TrySetResult(tunFd);

            using var waitHandle = new ManualResetEventSlim(false);
            using var reg = token.Register(() => waitHandle.Set());
            waitHandle.Wait();
        }
        catch (System.OperationCanceledException)
        {
            Logger.Log("VPN setup was cancelled");
            sessionTcs?.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Logger.Error($"VPN setup error: {ex.Message}");
            sessionTcs?.TrySetException(ex);
            StopFromService("error", broadcastStop: true);
        }
    }

    private static void ClearCloseOnExec(int fd)
    {
        try
        {
            int flags = fcntl(fd, FGetFd, 0);
            if (flags >= 0 && (flags & FdCloexec) != 0)
            {
                fcntl(fd, FSetFd, flags & ~FdCloexec);
                Logger.Log($"Cleared FD_CLOEXEC on fd={fd}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ClearCloseOnExec: {ex.Message}");
        }
    }

    private static void SaveVpnState(bool connected)
    {
        try
        {
            var prefs = global::Android.App.Application.Context
                .GetSharedPreferences("sanina_vpn", FileCreationMode.Private);
            var editor = prefs?.Edit();
            if (editor != null)
            {
                editor.PutBoolean("vpn_connected", connected);
                editor.Apply();
            }
        }
        catch { }
    }

    private string GetSafeString(int resId, string fallback)
    {
        try { return GetString(resId); }
        catch { return fallback; }
    }

    private Notification BuildNotification(string serverText)
    {
        var openIntent = new Intent(this, typeof(MainActivity));
        openIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendingFlags = OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent
            : PendingIntentFlags.UpdateCurrent;
        var openPendingIntent = PendingIntent.GetActivity(this, 1, openIntent, pendingFlags);

        var stopIntent = new Intent(this, typeof(SaninaVpnService));
        stopIntent.SetAction(ActionStop);
        var stopPendingIntent = PendingIntent.GetService(this, 0, stopIntent, pendingFlags);
        string disconnectLabel = GetSafeString(Resource.String.notif_disconnect, "Отключить");

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var action = new Notification.Action.Builder(
                global::Android.Graphics.Drawables.Icon.CreateWithResource(this, Android.Resource.Drawable.IcMenuCloseClearCancel),
                disconnectLabel,
                stopPendingIntent).Build();

            return new Notification.Builder(this, ChannelId)
                .SetContentTitle("Hivox")
                .SetContentText(serverText)
                .SetSmallIcon(Android.Resource.Drawable.IcLockLock)
                .SetContentIntent(openPendingIntent)
                .SetOngoing(true)
                .SetOnlyAlertOnce(true)
                .AddAction(action)
                .Build();
        }
        else
        {
#pragma warning disable CA1422
            return new Notification.Builder(this)
                .SetContentTitle("Hivox")!
                .SetContentText(serverText)!
                .SetSmallIcon(Android.Resource.Drawable.IcLockLock)!
                .SetContentIntent(openPendingIntent)!
                .SetOngoing(true)!
                .AddAction(Android.Resource.Drawable.IcMenuCloseClearCancel, disconnectLabel, stopPendingIntent)!
                .Build()!;
#pragma warning restore CA1422
        }
    }

    private void CreateNotificationChannel()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, "Hivox VPN", NotificationImportance.Low)
            {
                Description = "Статус VPN подключения"
            };
            var nm = GetSystemService(NotificationService) as NotificationManager;
            nm?.CreateNotificationChannel(channel);
        }
    }

    private void AcquireWakeLockIfNeeded()
    {
        try
        {
            ReleaseWakeLock();
            if (!Config.KeepWakeLock) return;
            var pm = GetSystemService(PowerService) as PowerManager;
            _wakeLock = pm?.NewWakeLock(WakeLockFlags.Partial, "Hivox::VpnWakeLock");
            _wakeLock?.SetReferenceCounted(false);
            _wakeLock?.Acquire();
        }
        catch (Exception ex)
        {
            Logger.Log($"WakeLock acquire warning: {ex.Message}");
        }
    }

    private void ReleaseWakeLock()
    {
        try
        {
            if (_wakeLock != null)
            {
                if (_wakeLock.IsHeld)
                    _wakeLock.Release();
                _wakeLock.Dispose();
                _wakeLock = null;
            }
        }
        catch { }
    }

    public static void UpdateSpeedNotification(string downSpeed, string upSpeed)
    {
        try
        {
            var svc = _activeInstance;
            if (svc == null || !IsRunning || !Config.NotifShowSpeed) return;
            string prefix = Config.NotifShowServer ? $"{svc._serverLabel}  •  " : "";
            string text = $"{prefix}↓ {downSpeed}  ↑ {upSpeed}";
            var nm = svc.GetSystemService(NotificationService) as NotificationManager;
            nm?.Notify(NotificationId, svc.BuildNotification(text));
        }
        catch { }
    }

    public static void RefreshRuntimeNotification()
    {
        try
        {
            var svc = _activeInstance;
            if (svc == null || !IsRunning) return;
            string notifText = Config.NotifShowServer
                ? svc._serverLabel
                : svc.GetSafeString(Resource.String.notif_protected_active, "Защищённое соединение активно");
            var nm = svc.GetSystemService(NotificationService) as NotificationManager;
            nm?.Notify(NotificationId, svc.BuildNotification(notifText));
        }
        catch { }
    }

    public static void ApplyRuntimeKeepAwake()
    {
        try
        {
            var svc = _activeInstance;
            if (svc == null || !IsRunning) return;
            svc.AcquireWakeLockIfNeeded();
        }
        catch { }
    }

    private sealed class ScreenOffReceiver : BroadcastReceiver
    {
        private readonly SaninaVpnService _svc;
        public ScreenOffReceiver(SaninaVpnService svc) => _svc = svc;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == Intent.ActionScreenOff && IsRunning && Config.DisconnectOnLock)
            {
                Logger.Log("Screen turned off -> stopping VPN (disconnect_on_lock=true)");
                _svc.StopFromService("screen_lock");
            }
        }
    }

    public static void StartVpn(Context context, string? serverName = null, bool enableIpv6 = false)
    {
        long gen = Interlocked.Increment(ref _requestedSessionGen);
        TunFdReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var intent = new Intent(context, typeof(SaninaVpnService));
        intent.PutExtra("session_gen", gen);
        if (!string.IsNullOrEmpty(serverName))
            intent.PutExtra("server_name", serverName);
        intent.PutExtra("ipv6", enableIpv6);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public static void StartBootConnect(Context context)
    {
        long gen = Interlocked.Increment(ref _requestedSessionGen);
        var intent = new Intent(context, typeof(SaninaVpnService));
        intent.SetAction(ActionBootConnect);
        intent.PutExtra("session_gen", gen);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public static void StopVpn(Context context, bool fromUi = false)
    {
        long latest = CurrentRequestedSessionGen;
        long prevCancelled = Volatile.Read(ref _cancelledUpToGen);
        if (latest > prevCancelled)
            Interlocked.Exchange(ref _cancelledUpToGen, latest);

        TunFdReady?.TrySetCanceled();

        var active = _activeInstance;
        if (!IsRunning && active == null)
            return;

        if (fromUi && active != null)
        {
            active.StopFromService("ui", broadcastStop: false);
            return;
        }

        var intent = new Intent(context, typeof(SaninaVpnService));
        intent.SetAction(ActionStop);
        intent.PutExtra("from_ui", fromUi);
        intent.PutExtra("session_gen", latest);
        context.StartService(intent);
    }
}
