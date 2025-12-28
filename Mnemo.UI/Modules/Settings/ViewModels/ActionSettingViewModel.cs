using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class ActionSettingViewModel : ViewModelBase
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _actionText;

    public ActionSettingViewModel(string title, string description, string actionText)
    {
        _title = title;
        _description = description;
        _actionText = actionText;
    }
}

