using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Components.Sidebar;
using Mnemo.UI.Components;

namespace Mnemo.UI.Modules;

public class CoreUIModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SidebarViewModel>();
        services.AddTransient<TopbarViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        // Global routes could go here if any
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        // Global sidebar items if any
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // Global AI tools if any
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
        // No widgets for core UI
    }
}

