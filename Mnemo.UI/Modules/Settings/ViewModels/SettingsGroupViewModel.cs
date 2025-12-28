using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SettingsGroupViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    public ObservableCollection<ViewModelBase> Items { get; } = new();

    public SettingsGroupViewModel(string name)
    {
        _name = name;
    }
}

