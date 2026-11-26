using CommunityToolkit.Maui;
using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;
using Hivox.Mobile.Views;
using ZXing.Net.Maui.Controls;

namespace Hivox.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Error($"UNHANDLED: {ex?.Message ?? "null"}");
            Logger.Error($"Stack: {ex?.StackTrace ?? "null"}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error($"UNOBSERVED TASK: {e.Exception?.Message ?? "null"}");
            e.SetObserved();
        };

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseBarcodeReader()
            .ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<Shell, PlatformAndroid.HivoxShellRenderer>();
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
            });

#if ANDROID
        Microsoft.Maui.Handlers.RadioButtonHandler.Mapper.AppendToMapping("HivoxRadioStyle", (handler, _) =>
        {
            if (handler.PlatformView is Android.Widget.RadioButton rb)
            {
                rb.Background = null;
                var primary = Application.Current?.Resources.TryGetValue("M3Primary", out var pObj) == true && pObj is Color pc
                    ? Android.Graphics.Color.Rgb((int)(pc.Red * 255), (int)(pc.Green * 255), (int)(pc.Blue * 255))
                    : Android.Graphics.Color.ParseColor("#7C4DFF");
                var outline = Application.Current?.Resources.TryGetValue("M3Outline", out var oObj) == true && oObj is Color oc
                    ? Android.Graphics.Color.Rgb((int)(oc.Red * 255), (int)(oc.Green * 255), (int)(oc.Blue * 255))
                    : Android.Graphics.Color.ParseColor("#9E93B8");
                var states = new int[][]
                {
                    new[] { Android.Resource.Attribute.StateChecked },
                    new int[0]
                };
                var colors = new int[] { primary, outline };
                rb.ButtonTintList = new Android.Content.Res.ColorStateList(states, colors);
            }
        });

        Microsoft.Maui.Handlers.CheckBoxHandler.Mapper.AppendToMapping("HivoxCheckBoxStyle", (handler, _) =>
        {
            if (handler.PlatformView is Android.Widget.CheckBox cb)
            {
                cb.Background = null;
            }
        });

        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("HivoxEntryStyle", (handler, _) =>
        {
            handler.PlatformView.Background = null;
            handler.PlatformView.SetPadding(4, 0, 4, 0);
            handler.PlatformView.SetIncludeFontPadding(false);
        });
#endif

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<ConnectionPage>();
        builder.Services.AddSingleton<ServersPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddTransient<SubscriptionsPage>();
        builder.Services.AddTransient<ThemePage>();
        builder.Services.AddTransient<IpTypePage>();
        builder.Services.AddTransient<InboundsPage>();
        builder.Services.AddTransient<SubscriptionSettingsPage>();
        builder.Services.AddTransient<PingSettingsPage>();
        builder.Services.AddTransient<DiagnosticsPage>();
        builder.Services.AddTransient<AboutPage>();
        builder.Services.AddTransient<AppProxyPage>();
        builder.Services.AddTransient<ConnectionSettingsPage>();
        builder.Services.AddTransient<TunnelSettingsPage>();
        builder.Services.AddTransient<BatteryOptimizationPage>();
        builder.Services.AddTransient<QrScannerPage>();
        builder.Services.AddTransient<GeoDataPage>();
        builder.Services.AddTransient<ConnectionCheckPage>();
        builder.Services.AddTransient<ProviderSettingsPage>();

        return builder.Build();
    }
}
