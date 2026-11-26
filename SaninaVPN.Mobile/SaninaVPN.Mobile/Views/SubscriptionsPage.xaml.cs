using Hivox.Mobile.ViewModels;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class SubscriptionsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isNavigating;
    public MainViewModel ViewModel => _vm;

    public SubscriptionsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }

    private void OnDeleteSubscriptionInvoked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is string url)
            _vm.DeleteSubscriptionCommand.Execute(url);
    }

    private void OnDeleteSubscriptionTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is string url)
            _vm.DeleteSubscriptionCommand.Execute(url);
    }

    private async void OnAddTapped(object? sender, TappedEventArgs e)
    {
        await PromptAndAddSubscriptionAsync();
    }

    private async void OnAddSubscriptionClicked(object? sender, EventArgs e)
    {
        await PromptAndAddSubscriptionAsync();
    }

    private async Task PromptAndAddSubscriptionAsync()
    {
        string? action = await HivoxDialog.ShowActionSheetAsync(
            "Добавить подписку", "Отмена", null,
            "Ввести URL подписки",
            "Сканировать QR-код / из галереи");

        if (action == "Сканировать QR-код / из галереи")
        {
            await Shell.Current.GoToAsync("qrscanner");
            return;
        }
        if (action != "Ввести URL подписки")
            return;

        string? url = await HivoxDialog.ShowPromptAsync(
            "Добавить подписку",
            "Введите URL подписки:",
            "Добавить", "Отмена",
            placeholder: "https://example.com/sub",
            keyboard: Keyboard.Url);

        if (!string.IsNullOrWhiteSpace(url))
            await _vm.AddSubscription(url);
    }

    private async void OnUpdateAllClicked(object? sender, EventArgs e)
    {
        await _vm.UpdateSubscriptions();
    }
}
