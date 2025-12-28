using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components;

public partial class TopbarViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;

    public ObservableCollection<TopbarButtonModel> Buttons { get; } = new();

    [ObservableProperty]
    private bool _isGamificationEnabled;

    [ObservableProperty]
    private string _profilePicturePath = "avares://Mnemo.UI/Assets/demo-profile-pic.png";

    public TopbarViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        
        // Initial load
        _isGamificationEnabled = _settingsService.GetAsync("App.EnableGamification", true).GetAwaiter().GetResult();
        _profilePicturePath = _settingsService.GetAsync("User.ProfilePicture", "avares://Mnemo.UI/Assets/demo-profile-pic.png").GetAwaiter().GetResult();

        // Listen for changes
        _settingsService.SettingChanged += (s, key) =>
        {
            if (key == "App.EnableGamification")
            {
                IsGamificationEnabled = _settingsService.GetAsync("App.EnableGamification", true).GetAwaiter().GetResult();
            }
            else if (key == "User.ProfilePicture")
            {
                ProfilePicturePath = _settingsService.GetAsync("User.ProfilePicture", "avares://Mnemo.UI/Assets/demo-profile-pic.png").GetAwaiter().GetResult();
            }
        };
    }
}



