using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services;

namespace Mnemo.UI.Services;

public static class Bootstrapper
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // 1. Register Core/Infrastructure Services
        services.AddSingleton<ILoggerService, LoggerService>();
        services.AddSingleton<ITaskScheduler, TaskSchedulerService>();
        services.AddSingleton<IStorageProvider, SqliteStorageProvider>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILateXEngine, LaTeXEngine>();
        
        // 2. Register UI-specific Services
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<INavigationRegistry>(sp => sp.GetRequiredService<NavigationService>());
        
        services.AddSingleton<SidebarService>();
        services.AddSingleton<ISidebarService>(sp => sp.GetRequiredService<SidebarService>());
        
        services.AddSingleton<IFunctionRegistry, FunctionRegistry>();

        // 3. Discover and Configure Modules
        // We scan for modules before building the final provider
        var modules = DiscoverModules();
        var registrar = new ServiceRegistrar(services);
        foreach (var module in modules)
        {
            module.ConfigureServices(registrar);
        }

        var serviceProvider = services.BuildServiceProvider();

        // 5. Register Routes, Sidebar Items and Tools
        var navRegistry = serviceProvider.GetRequiredService<INavigationRegistry>();
        var funcRegistry = serviceProvider.GetRequiredService<IFunctionRegistry>();
        var sidebarService = serviceProvider.GetRequiredService<ISidebarService>();

        foreach (var module in modules)
        {
            module.RegisterRoutes(navRegistry);
            module.RegisterSidebarItems(sidebarService);
            module.RegisterTools(funcRegistry);
        }

        return serviceProvider;
    }

    private static IEnumerable<IModule> DiscoverModules()
    {
        // For now, scan all loaded assemblies. 
        // In the future, we can add Assembly.LoadFrom for a Modules folder.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var moduleType = typeof(IModule);
        
        var foundModules = new List<IModule>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => moduleType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                
                foreach (var type in types)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IModule module)
                        {
                            foundModules.Add(module);
                        }
                    }
                    catch
                    {
                        // Module instantiation failures are ignored during discovery phase
                        // Logger will be available after provider is built if we need to log this
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be scanned
            }
        }
        return foundModules;
    }
}


