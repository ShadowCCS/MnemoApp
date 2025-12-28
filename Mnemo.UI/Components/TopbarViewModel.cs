using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Mnemo.UI.Components;

public partial class TopbarViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IOverlayService _overlayService;

    public ObservableCollection<TopbarButtonModel> Buttons { get; } = new();

    [ObservableProperty]
    private bool _isGamificationEnabled;

    [ObservableProperty]
    private string _profilePicturePath = "avares://Mnemo.UI/Assets/demo-profile-pic.png";

    public TopbarViewModel(ISettingsService settingsService, IOverlayService overlayService)
    {
        _settingsService = settingsService;
        _overlayService = overlayService;
        
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

    [RelayCommand]
    private async Task CloseAsync()
    {
        var result = await _overlayService.CreateDialogAsync(
            "Confirm Exit",
            "Are you sure you want to close Mnemo?",
            "Exit",
            "Cancel"
        );

        if (result == "Exit")
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }

    [RelayCommand]
    private void Minimize()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow!.WindowState = Avalonia.Controls.WindowState.Minimized;
        }
    }

    [RelayCommand]
    private void Maximize()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow!.WindowState = desktop.MainWindow.WindowState == Avalonia.Controls.WindowState.Maximized
                ? Avalonia.Controls.WindowState.Normal
                : Avalonia.Controls.WindowState.Maximized;
        }
    }
}



