using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class AppIconOptionViewModel : ViewModelBase
{
    private readonly AppIconSettingViewModel _parent;
    [ObservableProperty] private string _iconPath;
    [ObservableProperty] private bool _isSelected;

    public AppIconOptionViewModel(string iconPath, bool isSelected, AppIconSettingViewModel parent)
    {
        _iconPath = iconPath;
        _isSelected = isSelected;
        _parent = parent;
    }

    [RelayCommand]
    private async Task Select()
    {
        await _parent.SelectIconAsync(IconPath);
    }
}


