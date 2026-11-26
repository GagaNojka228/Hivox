using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile.Views;

public partial class AboutPage : ContentPage
{
    private bool _isNavigating;

    public AboutPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }
}
