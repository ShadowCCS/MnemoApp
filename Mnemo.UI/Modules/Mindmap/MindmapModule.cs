using System;
using Mnemo.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Infrastructure.Services;
using Mnemo.Infrastructure.Services.Tools;
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

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("mindmap", typeof(MindmapOverviewViewModel));
        registry.RegisterRoute("mindmap-detail", typeof(MindmapViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Mindmap", "mindmap", "avares://Mnemo.UI/Icons/Sidebar/mindmap.svg", "Library", 1, int.MaxValue);
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
        var svc = services.GetRequiredService<MindmapToolService>();
        MindmapToolRegistrar.Register(registry, svc);
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
    }
}
