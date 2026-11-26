using Hivox.Mobile.Models;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class ThemePage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isNavigating;

    public ThemePage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        Loaded += (_, _) =>
        {
            DarkRadio.IsChecked = vm.Theme == ThemeMode.Dark;
            BlackRadio.IsChecked = vm.Theme == ThemeMode.Black;
            LightRadio.IsChecked = vm.Theme == ThemeMode.Light;
            MaterialYouRadio.IsChecked = vm.Theme == ThemeMode.MaterialYou;
        };
    }

    private void OnDarkTapped(object? sender, TappedEventArgs e) => DarkRadio.IsChecked = true;
    private void OnBlackTapped(object? sender, TappedEventArgs e) => BlackRadio.IsChecked = true;
    private void OnLightTapped(object? sender, TappedEventArgs e) => LightRadio.IsChecked = true;
    private void OnMaterialYouTapped(object? sender, TappedEventArgs e) => MaterialYouRadio.IsChecked = true;

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
        _vm.Theme = BlackRadio.IsChecked ? ThemeMode.Black
                  : LightRadio.IsChecked ? ThemeMode.Light
                  : MaterialYouRadio.IsChecked ? ThemeMode.MaterialYou
                  : ThemeMode.Dark;
        await Shell.Current.GoToAsync("..");
    }
}
