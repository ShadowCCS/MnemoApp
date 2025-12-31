using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class ProfilePictureOptionViewModel : ViewModelBase
{
    private readonly ProfilePictureSettingViewModel _parent;
    [ObservableProperty] private string _imagePath;
    [ObservableProperty] private bool _isSelected;

    public ProfilePictureOptionViewModel(string imagePath, bool isSelected, ProfilePictureSettingViewModel parent)
    {
        _imagePath = imagePath;
        _isSelected = isSelected;
        _parent = parent;
    }

    [RelayCommand]
    private async Task Select()
    {
        await _parent.SelectPictureAsync(ImagePath);
    }
}