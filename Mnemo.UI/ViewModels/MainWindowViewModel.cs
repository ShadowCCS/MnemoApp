using Mnemo.UI.Services;

namespace Mnemo.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public INavigationService Navigation { get; }
    public ISidebarService Sidebar { get; }
    public Components.Sidebar.SidebarViewModel SidebarViewModel { get; }
    public Components.TopbarViewModel TopbarViewModel { get; }

    public MainWindowViewModel(INavigationService navigation, ISidebarService sidebar, Components.Sidebar.SidebarViewModel sidebarViewModel, Components.TopbarViewModel topbarViewModel)
    {
        Navigation = navigation;
        Sidebar = sidebar;
        SidebarViewModel = sidebarViewModel;
        TopbarViewModel = topbarViewModel;
    }
}

