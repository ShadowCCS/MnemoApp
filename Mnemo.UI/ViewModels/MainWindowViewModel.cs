using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Services;
using Mnemo.UI.Services;

namespace Mnemo.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    public INavigationService Navigation { get; }
    public ISidebarService Sidebar { get; }
    public Components.Sidebar.SidebarViewModel SidebarViewModel { get; }
    public Components.TopbarViewModel TopbarViewModel { get; }

    [ObservableProperty]
    private string _appIconPath = "avares://Mnemo.UI/Assets/AppIcons/AppIconDarkClassic.ico";

    public MainWindowViewModel(
        INavigationService navigation, 
        ISidebarService sidebar, 
        Components.Sidebar.SidebarViewModel sidebarViewModel, 
        Components.TopbarViewModel topbarViewModel,
        ISettingsService settingsService)
    {
        Navigation = navigation;
        Sidebar = sidebar;
        SidebarViewModel = sidebarViewModel;
        TopbarViewModel = topbarViewModel;
        _settingsService = settingsService;

        // Load initial app icon asynchronously
        _ = LoadSettingsAsync();

        // Listen for setting changes
        _settingsService.SettingChanged += (s, key) =>
        {
            if (key == "App.Icon")
            {
                _ = LoadSettingsAsync();
            }
        };
    }

    private async Task LoadSettingsAsync()
    {
        AppIconPath = await _settingsService.GetAsync("App.Icon", "avares://Mnemo.UI/Assets/AppIcons/AppIconDarkClassic.ico");
    }
}

