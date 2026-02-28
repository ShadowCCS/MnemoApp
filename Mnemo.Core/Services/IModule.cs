namespace Mnemo.Core.Services;

/// <summary>
/// Defines a module that can register services, routes, widgets, and AI tools.
/// </summary>
public interface IModule
{
    void ConfigureServices(IServiceRegistrar services);
    /// <summary>
    /// Registers translation sources for this module. Called before the service provider is built.
    /// </summary>
    void RegisterTranslationSources(ITranslationSourceRegistry registry);
    void RegisterRoutes(INavigationRegistry registry);
    void RegisterSidebarItems(ISidebarService sidebarService);
    void RegisterTools(IFunctionRegistry registry);
    void RegisterWidgets(IWidgetRegistry registry);
}

