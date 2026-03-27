using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Components.Sidebar;
using Mnemo.UI.Components.RightSidebar;
using Mnemo.UI.Components;
using Mnemo.UI.Services;

namespace Mnemo.UI.Modules;

public class CoreUIModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddSingleton<ChatPauseToSendEstimator>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SidebarViewModel>();
        services.AddTransient<RightSidebarViewModel>();
        services.AddTransient<TopbarViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
        // No translations for core UI
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        // Global routes could go here if any
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        // Global sidebar items if any
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
        // Global AI tools if any
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
        // No widgets for core UI
    }
}

