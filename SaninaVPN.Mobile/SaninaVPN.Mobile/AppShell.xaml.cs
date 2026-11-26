using Hivox.Mobile.Views;

namespace Hivox.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("subscriptions", typeof(SubscriptionsPage));
        Routing.RegisterRoute("theme", typeof(ThemePage));
        Routing.RegisterRoute("iptype", typeof(IpTypePage));
        Routing.RegisterRoute("inbounds", typeof(InboundsPage));
        Routing.RegisterRoute("subsettings", typeof(SubscriptionSettingsPage));
        Routing.RegisterRoute("pingsettings", typeof(PingSettingsPage));
        Routing.RegisterRoute("diagnostics", typeof(DiagnosticsPage));
        Routing.RegisterRoute("about", typeof(AboutPage));
        Routing.RegisterRoute("appproxy", typeof(AppProxyPage));
        Routing.RegisterRoute("connsettings", typeof(ConnectionSettingsPage));
        Routing.RegisterRoute("tunnelsettings", typeof(TunnelSettingsPage));
        Routing.RegisterRoute("battery", typeof(BatteryOptimizationPage));
        Routing.RegisterRoute("qrscanner", typeof(QrScannerPage));
        Routing.RegisterRoute("geodata", typeof(GeoDataPage));
        Routing.RegisterRoute("conncheck", typeof(ConnectionCheckPage));
        Routing.RegisterRoute("providersettings", typeof(ProviderSettingsPage));
    }
}
