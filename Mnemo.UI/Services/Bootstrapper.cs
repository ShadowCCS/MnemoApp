using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services;

using Mnemo.Infrastructure.Services.AI;
using Mnemo.Infrastructure.Services.Knowledge;

namespace Mnemo.UI.Services;

public static class Bootstrapper
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // 1. Register Core/Infrastructure Services
        services.AddSingleton<ILoggerService, LoggerService>();
        services.AddSingleton<IStorageProvider, SqliteStorageProvider>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILaTeXEngine, LaTeXEngine>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();

        // AI Services
        services.AddSingleton<HardwareDetector>();
        services.AddSingleton<IAIModelRegistry, ModelRegistry>();
        services.AddSingleton<IResourceGovernor, ResourceGovernor>();
        services.AddSingleton<LlamaCppServerManager>();
        services.AddSingleton<IAIServerManager>(sp => sp.GetRequiredService<LlamaCppServerManager>());
        services.AddSingleton<ITextGenerationService, LlamaCppHttpTextService>();
        services.AddSingleton<IAIOrchestrator, AIOrchestrator>();
        services.AddSingleton<IAITaskManager, AITaskManager>();

        // Knowledge/RAG Services
        services.AddSingleton<IVectorStore, SqliteVectorStore>();
        services.AddSingleton<IEmbeddingService, OnnxEmbeddingService>();
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddSingleton<ILearningPathService, LearningPathService>();
        
        // 2. Register UI-specific Services
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IOverlayService, OverlayService>();
        services.AddSingleton<IUIService, UIService>();
        
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<INavigationRegistry>(sp => sp.GetRequiredService<NavigationService>());
        
        services.AddSingleton<SidebarService>();
        services.AddSingleton<ISidebarService>(sp => sp.GetRequiredService<SidebarService>());
        
        services.AddSingleton<IFunctionRegistry, FunctionRegistry>();
        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();

        // 3. Discover and Configure Modules
        // We scan for modules before building the final provider
        var modules = DiscoverModules();
        var registrar = new ServiceRegistrar(services);
        foreach (var module in modules)
        {
            module.ConfigureServices(registrar);
        }

        var serviceProvider = services.BuildServiceProvider();

        // 4. Initialize AI Model Registry and set default server path
        var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _ = Task.Run(async () =>
        {
            // Set default llama.cpp server path if not configured
            var serverPath = await settingsService.GetAsync<string>("AI.LlamaCpp.ServerPath");
            if (string.IsNullOrEmpty(serverPath))
            {
                var defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "mnemo",
                    "models",
                    "llamaServer",
                    "llama-server.exe");
                
                await settingsService.SetAsync("AI.LlamaCpp.ServerPath", defaultPath);
            }
        });

        var modelRegistry = serviceProvider.GetRequiredService<IAIModelRegistry>();
        _ = modelRegistry.RefreshAsync();

        // 5. Auto-start router model server
        var serverManager = serviceProvider.GetRequiredService<LlamaCppServerManager>();
        var logger = serviceProvider.GetRequiredService<ILoggerService>();
        
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait a moment for model registry to complete
                await Task.Delay(500);
                
                var models = await modelRegistry.GetAvailableModelsAsync();
                var routerModel = models.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == "router");
                
                if (routerModel != null)
                {
                    logger.Info("Bootstrapper", "Auto-starting router model server...");
                    await serverManager.EnsureRunningAsync(routerModel, CancellationToken.None);
                    logger.Info("Bootstrapper", "Router model server started successfully.");
                }
                else
                {
                    logger.Warning("Bootstrapper", "No router model found. Router auto-start skipped.");
                }
            }
            catch (Exception ex)
            {
                logger.Error("Bootstrapper", "Failed to auto-start router model", ex);
            }
        });

        // 6. Register Routes, Sidebar Items, Tools and Widgets
        var navRegistry = serviceProvider.GetRequiredService<INavigationRegistry>();
        var funcRegistry = serviceProvider.GetRequiredService<IFunctionRegistry>();
        var sidebarService = serviceProvider.GetRequiredService<ISidebarService>();
        var widgetRegistry = serviceProvider.GetRequiredService<IWidgetRegistry>();

        foreach (var module in modules)
        {
            module.RegisterRoutes(navRegistry);
            module.RegisterSidebarItems(sidebarService);
            module.RegisterTools(funcRegistry);
            module.RegisterWidgets(widgetRegistry);
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


