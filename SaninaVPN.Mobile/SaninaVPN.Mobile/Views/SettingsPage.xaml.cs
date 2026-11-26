using Hivox.Mobile.ViewModels;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    private readonly MainViewModel _vm;

    public SettingsPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.ApplyRuntimeSettings();
    }

    private async void OnAppProxyTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("appproxy");

    private async void OnThemeTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("theme");

    private async void OnLanguageTapped(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var ctx = global::Android.App.Application.Context;
                var intent = new global::Android.Content.Intent(
                    global::Android.Provider.Settings.ActionAppLocaleSettings,
                    global::Android.Net.Uri.Parse($"package:{ctx.PackageName}"));
                intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                ctx.StartActivity(intent);
                return;
            }
            catch { }
        }

        await HivoxDialog.ShowAlertAsync(
            "Язык приложения",
            "На Android 13 и выше язык приложения выбирается в системных настройках: Настройки → Приложения → Hivox → Язык (поддерживаются Русский и English).",
            "OK");
    }

    private async void OnBatteryOptimizationTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("battery");

    private async void OnConnectionSettingsTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("connsettings");

    private async void OnTunnelSettingsTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("tunnelsettings");

    private async void OnSubscriptionsTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("subscriptions");

    private async void OnSubSettingsTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("subsettings");

    private async void OnPingSettingsTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("pingsettings");

    private async void OnAboutTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("about");

    private async void OnDiagnosticsTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("diagnostics");

    private async void OnGeoDataTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("geodata");

    private async void OnConnectionCheckTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("conncheck");

    private async void OnUrlSchemesTapped(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync(
            "Схемы URL: hivox://, vless://, vmess://, trojan://, ss://, hy2://",
            "Закрыть",
            null,
            "Скопировать шаблон hivox://import?url=",
            "Импортировать ссылку из буфера обмена");

        if (action == "Скопировать шаблон hivox://import?url=")
        {
            await Clipboard.Default.SetTextAsync("hivox://import?url=");
            await HivoxDialog.ShowAlertAsync("Скопировано", "Шаблон deep-link скопирован в буфер обмена.", "OK");
        }
        else if (action == "Импортировать ссылку из буфера обмена")
        {
            string? clip = await Clipboard.Default.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(clip))
                await _vm.ImportMassTextAsync(clip);
            else
                await HivoxDialog.ShowAlertAsync("Буфер обмена пуст", "Скопируйте ссылку сервера или подписки.", "OK");
        }
    }

    private async void OnBackupTapped(object? sender, EventArgs e)
    {
        string? action = await HivoxDialog.ShowActionSheetAsync(
            "Резервное копирование настроек и подписок",
            "Отмена",
            null,
            "Экспорт в буфер обмена (JSON)",
            "Импорт из буфера обмена (JSON)");

        if (action == "Экспорт в буфер обмена (JSON)")
        {
            string json = _vm.ExportBackupJson();
            await Clipboard.Default.SetTextAsync(json);
            await HivoxDialog.ShowAlertAsync("Резервная копия создана",
                "Полная резервная копия настроек, подписок и серверов скопирована в буфер обмена.", "OK");
        }
        else if (action == "Импорт из буфера обмена (JSON)")
        {
            string? clip = await Clipboard.Default.GetTextAsync();
            if (string.IsNullOrWhiteSpace(clip))
            {
                await HivoxDialog.ShowAlertAsync("Буфер обмена пуст", "Скопируйте JSON резервной копии Hivox и попробуйте снова.", "OK");
                return;
            }

            bool ok = await _vm.ImportBackupJsonAsync(clip);
            if (ok)
                await HivoxDialog.ShowAlertAsync("Восстановлено", "Настройки и серверы успешно восстановлены из резервной копии.", "OK");
            else
                await HivoxDialog.ShowAlertAsync("Ошибка", "Данные в буфере обмена не являются корректной резервной копией Hivox.", "OK");
        }
    }

    private async void OnResetSettingsTapped(object? sender, EventArgs e)
    {
        bool confirm = await HivoxDialog.ShowConfirmAsync(
            "Сбросить настройки?",
            "Все параметры туннеля, прокси и интерфейса будут возвращены к значениям по умолчанию. Ваши серверы и подписки сохранятся.",
            "Сбросить", "Отмена");

        if (!confirm) return;

        _vm.ResetSettings();
        await HivoxDialog.ShowAlertAsync("Готово", "Настройки сброшены по умолчанию.", "OK");
    }
}
