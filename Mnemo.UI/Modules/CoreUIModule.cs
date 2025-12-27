using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Components.Sidebar;

namespace Mnemo.UI.Modules;

public class CoreUIModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SidebarViewModel>();
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
}

