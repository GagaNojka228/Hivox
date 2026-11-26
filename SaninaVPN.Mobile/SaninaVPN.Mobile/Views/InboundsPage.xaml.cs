using Hivox.Mobile.ViewModels;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class InboundsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isNavigating;

    public InboundsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        Loaded += (_, _) =>
        {
            HttpSwitch.IsToggled = vm.GetBool("http_enabled", true);
            SocksSwitch.IsToggled = vm.GetBool("socks_enabled", true);
            HttpPortEntry.Text = vm.GetInt("http_port", 10809).ToString();
            SocksPortEntry.Text = vm.GetInt("socks_port", 10808).ToString();
        };
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isNavigating) return;
        if (!HttpSwitch.IsToggled && !SocksSwitch.IsToggled)
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", "Хотя бы один локальный inbound (HTTP или SOCKS5) должен быть включён.", "OK");
            return;
        }
        if (!int.TryParse(HttpPortEntry.Text?.Trim(), out int hp) || hp is < 1024 or > 65535)
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", "HTTP порт должен быть от 1024 до 65535.", "OK");
            return;
        }
        if (!int.TryParse(SocksPortEntry.Text?.Trim(), out int sp) || sp is < 1024 or > 65535)
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", "SOCKS порт должен быть от 1024 до 65535.", "OK");
            return;
        }
        if (hp == sp)
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", "HTTP и SOCKS порты не могут совпадать.", "OK");
            return;
        }

        bool changed =
            _vm.GetBool("http_enabled", true) != HttpSwitch.IsToggled ||
            _vm.GetBool("socks_enabled", true) != SocksSwitch.IsToggled ||
            _vm.GetInt("http_port", 10809) != hp ||
            _vm.GetInt("socks_port", 10808) != sp;

        _isNavigating = true;
        _vm.Settings["http_enabled"] = HttpSwitch.IsToggled;
        _vm.Settings["socks_enabled"] = SocksSwitch.IsToggled;
        _vm.Settings["http_port"] = hp;
        _vm.Settings["socks_port"] = sp;
        _vm.ApplyRuntimeSettings();
        _vm.SaveData();
        if (changed && _vm.IsConnected)
            _ = _vm.RestartActiveConnectionAsync();
        await Shell.Current.GoToAsync("..");
    }
}
