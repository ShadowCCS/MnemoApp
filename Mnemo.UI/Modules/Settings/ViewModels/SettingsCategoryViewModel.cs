using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SettingsCategoryViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _icon;
    [ObservableProperty] private bool _isSelected;
    public ObservableCollection<SettingsGroupViewModel> Groups { get; } = new();

    public SettingsCategoryViewModel(string name, string icon)
    {
        _name = name;
        _icon = icon;
    }
}

