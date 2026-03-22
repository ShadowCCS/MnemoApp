using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SettingsCategoryViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _icon;
    [ObservableProperty] private bool _isSelected;
    /// <summary>Optional subtitle under the category title; when null, the view uses the default settings blurb.</summary>
    public string? Subtitle { get; init; }
    /// <summary>Stable id used when refreshing categories (e.g. on language change).</summary>
    public string CategoryId { get; }
    public ObservableCollection<SettingsGroupViewModel> Groups { get; } = new();

    public SettingsCategoryViewModel(string name, string icon, string? categoryId = null)
    {
        _name = name;
        _icon = icon;
        CategoryId = categoryId ?? name;
    }
}

