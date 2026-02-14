namespace Mnemo.Core.Services;

/// <summary>
/// Defines a module that can register services, routes, widgets, and AI tools.
/// </summary>
public interface IModule
{
    void ConfigureServices(IServiceRegistrar services);
    void RegisterRoutes(INavigationRegistry registry);
    void RegisterSidebarItems(ISidebarService sidebarService);
    void RegisterTools(IFunctionRegistry registry);
    void RegisterWidgets(IWidgetRegistry registry);
}

