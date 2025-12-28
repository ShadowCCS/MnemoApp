using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class AppIconSettingViewModel : ViewModelBase
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;

    public AppIconSettingViewModel(string title, string description)
    {
        _title = title;
        _description = description;
    }
}

