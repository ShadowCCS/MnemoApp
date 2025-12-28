using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class ProfilePictureSettingViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private const string SettingKey = "User.ProfilePicture";

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    
    [ObservableProperty] private string _currentPicturePath = "avares://Mnemo.UI/Assets/demo-profile-pic.png";

    public ObservableCollection<ProfilePictureOptionViewModel> Options { get; } = new();

    public ProfilePictureSettingViewModel(ISettingsService settingsService, string title, string description)
    {
        _settingsService = settingsService;
        _title = title;
        _description = description;

        // Load current
        _currentPicturePath = _settingsService.GetAsync(SettingKey, "avares://Mnemo.UI/Assets/demo-profile-pic.png").GetAwaiter().GetResult();

        // Initialize options
        for (int i = 0; i <= 5; i++)
        {
            var path = $"avares://Mnemo.UI/Assets/ProfilePictures/img{i}.png";
            Options.Add(new ProfilePictureOptionViewModel(path, path == _currentPicturePath, this));
        }
    }

    public async Task SelectPictureAsync(string path)
    {
        CurrentPicturePath = path;
        foreach (var option in Options)
        {
            option.IsSelected = option.ImagePath == path;
        }
        await _settingsService.SetAsync(SettingKey, path);
    }
}

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
