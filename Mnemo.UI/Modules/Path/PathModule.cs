using Mnemo.Core.Services;
using Mnemo.UI.Modules.Path.ViewModels;

namespace Mnemo.UI.Modules.Path;

public class PathModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<PathViewModel>();
        services.AddTransient<PathDetailViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("path", typeof(PathViewModel));
        registry.RegisterRoute("path-detail", typeof(PathDetailViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Learning Path", "path", "avares://Mnemo.UI/Icons/Tabler/Used/Outlined/route-square.svg", "Main hub", 0, 1);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // No tools for path yet
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
        // No widgets for path
    }
}

