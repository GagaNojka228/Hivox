using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;

namespace Hivox.Mobile;

public partial class App : Application
{
    private readonly MainViewModel _vm;

    public App(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ThemeManager.ApplyThemeResources(vm.Theme);
        RequestedThemeChanged += (_, _) =>
        {
            if (_vm.Theme == Models.ThemeMode.MaterialYou)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ThemeManager.ApplyThemeResources(Models.ThemeMode.MaterialYou);
                    _vm.RefreshThemeBrushes();
                });
            }
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        ThemeManager.ApplyThemeResources(_vm.Theme);
        window.Activated += (_, _) => ThemeManager.ApplyThemeResources(_vm.Theme);
        return window;
    }
}
