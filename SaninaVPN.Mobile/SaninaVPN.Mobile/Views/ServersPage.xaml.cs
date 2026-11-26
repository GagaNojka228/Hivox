using Hivox.Mobile.ViewModels;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class ServersPage : ContentPage
{
    private readonly MainViewModel _vm;
    public MainViewModel ViewModel => _vm;

    public ServersPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    private void OnServerTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Models.ServerInfo server)
            _vm.ConnectToServerCommand.Execute(server);
    }

    private void OnCopyServerInvoked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Models.ServerInfo server)
            _vm.CopyServerUriCommand.Execute(server);
    }

    private void OnDeleteServerInvoked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Models.ServerInfo server)
            _vm.DeleteServerCommand.Execute(server);
    }

    private async void OnSortTapped(object? sender, TappedEventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync(
            "Сортировка и выбор", "Отмена", null,
            "По пингу (быстрые сверху)",
            "По имени (А–Я)",
            "Выбрать лучший сервер");

        switch (action)
        {
            case "По пингу (быстрые сверху)":
                _vm.SortByPing();
                break;
            case "По имени (А–Я)":
                _vm.SortByName();
                break;
            case "Выбрать лучший сервер":
                _vm.SelectBestServer();
                break;
        }
    }

    private async void OnAddServerTapped(object? sender, TappedEventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync(
            "Добавить серверы", "Отмена", null,
            "Вставить ссылку (vless://, vmess://...)",
            "Импорт из буфера обмена",
            "Сканировать QR-код / из галереи");

        if (action == "Вставить ссылку (vless://, vmess://...)")
        {
            string? uri = await HivoxDialog.ShowPromptAsync(
                "Добавить сервер", "Введите URI сервера:", "Добавить", "Отмена",
                placeholder: "vless://...");
            if (!string.IsNullOrWhiteSpace(uri))
                await _vm.AddServerUri(uri);
        }
        else if (action == "Импорт из буфера обмена")
        {
            string? clip = await Clipboard.Default.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(clip))
                await _vm.ImportMassTextAsync(clip);
            else
                await _vm.ShowAlert("Буфер пуст", "Скопируйте ссылки серверов или URL подписки.");
        }
        else if (action == "Сканировать QR-код / из галереи")
        {
            await Shell.Current.GoToAsync("qrscanner");
        }
    }
}
