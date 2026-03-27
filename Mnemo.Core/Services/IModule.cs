using System;

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
    /// <summary>
    /// Registers AI tools. <paramref name="services"/> provides access to resolved services
    /// (e.g. INoteService) needed to implement tool handlers.
    /// </summary>
    void RegisterTools(IFunctionRegistry registry, IServiceProvider services);
    void RegisterWidgets(IWidgetRegistry registry);
}

