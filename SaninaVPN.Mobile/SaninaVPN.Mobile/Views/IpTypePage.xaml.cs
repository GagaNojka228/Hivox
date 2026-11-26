using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class IpTypePage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isNavigating;

    public IpTypePage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        Loaded += (_, _) =>
        {
            var ipType = vm.GetString("preferred_ip", "auto");
            AutoRadio.IsChecked = ipType == "auto";
            Ipv4Radio.IsChecked = ipType == "ipv4";
            Ipv6Radio.IsChecked = ipType == "ipv6";
        };
    }

    private void OnAutoRowTapped(object? sender, TappedEventArgs e) => AutoRadio.IsChecked = true;
    private void OnIpv4RowTapped(object? sender, TappedEventArgs e) => Ipv4Radio.IsChecked = true;
    private void OnIpv6RowTapped(object? sender, TappedEventArgs e) => Ipv6Radio.IsChecked = true;
    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        string selected = AutoRadio.IsChecked ? "auto" : Ipv4Radio.IsChecked ? "ipv4" : "ipv6";
        bool changed = _vm.GetString("preferred_ip", "auto") != selected;
        _vm.SetSetting("preferred_ip", selected);
        _vm.ApplyRuntimeSettings();
        if (changed && _vm.IsConnected)
            _ = _vm.RestartActiveConnectionAsync();
        await Shell.Current.GoToAsync("..");
    }
}
