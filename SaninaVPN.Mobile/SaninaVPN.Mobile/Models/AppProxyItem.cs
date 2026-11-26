using CommunityToolkit.Mvvm.ComponentModel;

namespace Hivox.Mobile.Models;

public partial class AppProxyItem : ObservableObject
{
    public string PackageName { get; set; } = "";
    public string AppName { get; set; } = "";
    public bool IsSystemApp { get; set; }
    public string InitialLetter => !string.IsNullOrWhiteSpace(AppName)
        ? AppName.Trim()[..1].ToUpperInvariant()
        : "A";

    [ObservableProperty]
    private bool _isSelected;
}
