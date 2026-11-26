using Android.Content;
using Android.OS;
using Hivox.Mobile.ViewModels;
using AndroidSettings = Android.Provider.Settings;
using AndroidUri = Android.Net.Uri;

using Hivox.Mobile.Services;

namespace Hivox.Mobile.Views;

public partial class BatteryOptimizationPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _isNavigating;

    public BatteryOptimizationPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isNavigating = false;
        UpdateStatusVisual();
    }

    private void UpdateStatusVisual()
    {
        bool isIgnoring = false;
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                var ctx = global::Android.App.Application.Context;
                var pm = ctx.GetSystemService(Context.PowerService) as PowerManager;
                isIgnoring = pm != null && pm.IsIgnoringBatteryOptimizations(ctx.PackageName);
            }
            else
            {
                isIgnoring = true;
            }
        }
        catch { }

        Color Resolve(string key, string fallback) =>
            Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
                ? c
                : Color.FromArgb(fallback);

        if (isIgnoring)
        {
            var green = Resolve("M3Green", "#4ADE80");
            var greenContainer = Resolve("M3GreenContainer", "#123426");
            StatusBadge.BackgroundColor = greenContainer;
            StatusBadge.Stroke = new SolidColorBrush(green.WithAlpha(0.45f));
            StatusIcon.Text = "\uE876";
            StatusIcon.TextColor = green;
            StatusTitle.Text = "Оптимизация отключена";
            StatusDescription.Text = "Hivox добавлен в исключения экономии заряда и может работать в фоне без ограничений.";
        }
        else
        {
            var amber = Resolve("M3Amber", "#FBBF24");
            StatusBadge.BackgroundColor = Color.FromArgb("#24F59E0B");
            StatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#44F59E0B"));
            StatusIcon.Text = "\uE1A5";
            StatusIcon.TextColor = amber;
            StatusTitle.Text = "Включена оптимизация батареи";
            StatusDescription.Text = "Система может усыплять фоновый туннель при блокировке экрана. Рекомендуем отключить оптимизацию для Hivox.";
        }
    }

    private async void OnOpenBatterySettingsClicked(object? sender, EventArgs e)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                var intent = new Intent(AndroidSettings.ActionIgnoreBatteryOptimizationSettings);
                intent.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(intent);
                return;
            }
            OpenAppDetails();
        }
        catch (ActivityNotFoundException)
        {
            OpenAppDetails();
        }
        catch (Exception ex)
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", $"Не удалось открыть настройки батареи: {ex.Message}", "OK");
        }
    }

    private async void OnOpenAppDetailsClicked(object? sender, EventArgs e)
    {
        try
        {
            OpenAppDetails();
        }
        catch (Exception ex)
        {
            await HivoxDialog.ShowAlertAsync("Ошибка", $"Не удалось открыть свойства приложения: {ex.Message}", "OK");
        }
    }

    private static void OpenAppDetails()
    {
        var ctx = global::Android.App.Application.Context;
        var intent = new Intent(
            AndroidSettings.ActionApplicationDetailsSettings,
            AndroidUri.Parse($"package:{ctx.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        ctx.StartActivity(intent);
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        _vm.ApplyRuntimeSettings();
        await Shell.Current.GoToAsync("..");
    }
}
