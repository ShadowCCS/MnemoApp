using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services;
using Mnemo.UI.Modules.Mindmap.ViewModels;

namespace Mnemo.UI.Modules.Mindmap;

public class MindmapModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddSingleton<IMindmapService, MindmapService>();
        services.AddTransient<MindmapViewModel>();
        services.AddTransient<MindmapOverviewViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("mindmap", typeof(MindmapOverviewViewModel));
        registry.RegisterRoute("mindmap-detail", typeof(MindmapViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Mindmap", "mindmap", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/sitemap.svg", "Library", 1);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // No tools for overview yet
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
        // No widgets for mindmap
    }
}

