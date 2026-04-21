using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SettingsGroupViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isCollapsible;
    [ObservableProperty] private bool _isExpanded = true;
    public ObservableCollection<ViewModelBase> Items { get; } = new();

    public SettingsGroupViewModel(string name, bool isCollapsible = false, bool isExpanded = true)
    {
        _name = name;
        _isCollapsible = isCollapsible;
        _isExpanded = isExpanded;
    }
}

