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

using Mnemo.Core.History;
using Mnemo.Infrastructure.History;
using Mnemo.Infrastructure.Services.AI;
using Mnemo.Infrastructure.Services.AI.PlatformHardware;
using Mnemo.Infrastructure.Services.Knowledge;
using Mnemo.Infrastructure.Services.Notes;
using Mnemo.Infrastructure.Services.Speech;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.UI.Services;

public static class Bootstrapper
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // 1. Register Core/Infrastructure Services
        services.AddSingleton<IHistoryManager, HistoryManager>();
        services.AddSingleton<ILoggerService, LoggerService>();
        services.AddSingleton<IStorageProvider, SqliteStorageProvider>();
        services.AddSingleton<IChatModuleHistoryService, ChatModuleHistoryService>();
        services.AddSingleton<IChatHistoryClearService, ChatHistoryClearService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IChatDatasetLogger, ChatDatasetLogger>();
        services.AddSingleton<DatasetExporter>();
        services.AddSingleton<ILaTeXEngine, LaTeXEngine>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
        services.AddSingleton<INoteClipboardPayloadCodec, NoteClipboardPayloadCodec>();
        services.AddSingleton<INoteClipboardPlatformService, NoteClipboardPlatformService>();

        // AI Services
        services.AddSingleton<IPlatformHardwareGpuProvider>(sp =>
            PlatformHardwareGpuProviderFactory.Create(sp.GetRequiredService<ILoggerService>()));
        services.AddSingleton<HardwareDetector>();
        services.AddSingleton<IAIModelRegistry, ModelRegistry>();
        services.AddSingleton<IAIModelsSetupService, AIModelsSetupService>();
        services.AddSingleton<IResourceGovernor, ResourceGovernor>();
        services.AddSingleton<LlamaCppServerManager>();
        services.AddSingleton<IAIServerManager>(sp => sp.GetRequiredService<LlamaCppServerManager>());
        services.AddSingleton<LlamaCppHttpTextService>();
        services.AddSingleton<ITeacherModelClient, VertexGeminiTeacherClient>();
        services.AddSingleton<ITextGenerationService>(sp => new DelegatingTextGenerationService(
            sp.GetRequiredService<LlamaCppHttpTextService>(),
            sp.GetRequiredService<ITeacherModelClient>(),
            sp.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IHardwareTierEvaluator, HardwareTierEvaluator>();
        services.AddSingleton<ISkillRegistry, SkillRegistry>();
        services.AddSingleton<ISkillSystemPromptComposer, SkillSystemPromptComposer>();
        services.AddSingleton<IOrchestrationLayer, OrchestrationLayerService>();
        services.AddSingleton<IToolResultFormatter, ToolResultFormatter>();
        services.AddSingleton<IMainThreadDispatcher, AvaloniaMainThreadDispatcher>();
        services.AddSingleton(sp => new NotesToolService(
            sp.GetRequiredService<INoteService>(),
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<IMainThreadDispatcher>(),
            sp.GetRequiredService<IKnowledgeService>()));
        services.AddSingleton<ApplicationToolService>();
        services.AddSingleton<IToolDispatchAmbient, ToolDispatchAmbient>();
        services.AddSingleton<ISkillInjectionOverrideStore, SkillInjectionOverrideStore>();
        services.AddSingleton<SkillDiscoveryToolService>();
        services.AddSingleton<SettingsToolService>();
        services.AddSingleton(sp => new MindmapToolService(
            sp.GetRequiredService<IMindmapService>(),
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<IMainThreadDispatcher>()));
        services.AddSingleton<IToolDispatcher, ToolDispatcher>();
        services.AddSingleton<IRoutingToolHintStore, RoutingToolHintStore>();

        // Conversation Memory System
        services.AddSingleton<IConversationMemoryStore>(sp =>
            new ConversationMemoryStore(sp.GetRequiredService<ILoggerService>()));
        services.AddSingleton<IConversationSummarizer, ConversationSummarizer>();
        services.AddSingleton<IConversationLongTermMemoryEmbedder, ConversationLongTermMemoryEmbedder>();
        // Module-specific memory extractors registered below in RegisterTools;
        // the composite is built after module discovery so all extractors are included.
        services.AddSingleton<NotesMemoryExtractor>();
        services.AddSingleton<MindmapMemoryExtractor>();
        services.AddSingleton<IToolResultMemoryExtractor>(sp =>
            new CompositeToolResultMemoryExtractor(new IToolResultMemoryExtractor[]
            {
                sp.GetRequiredService<NotesMemoryExtractor>(),
                sp.GetRequiredService<MindmapMemoryExtractor>()
            }));
        services.AddSingleton<IConversationMemoryInjector>(sp =>
            new ConversationMemoryInjector(
                sp.GetRequiredService<IConversationMemoryStore>(),
                sp.GetRequiredService<IKnowledgeService>(),
                sp.GetRequiredService<ILoggerService>()));

        services.AddSingleton<IAIOrchestrator, AIOrchestrator>();
        services.AddSingleton<IAITaskManager, AITaskManager>();

        // Knowledge/RAG Services
        services.AddSingleton<IVectorStore, SqliteVectorStore>();
        services.AddSingleton<IEmbeddingService, OnnxEmbeddingService>();
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddSingleton<ILearningPathService, LearningPathService>();
        services.AddSingleton<INoteService, NoteService>();
        services.AddSingleton<INoteFolderService, NoteFolderService>();
        services.AddSingleton<ISpeechRecognitionService, WhisperSpeechRecognitionService>();

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

        // 3. Discover modules and register translation sources (before building provider)
        var modules = DiscoverModules();
        var translationRegistry = new TranslationSourceRegistry();
        translationRegistry.Add(new EmbeddedBuiltInTranslationSource());
        foreach (var module in modules)
        {
            module.RegisterTranslationSources(translationRegistry);
        }
        services.AddSingleton<ILocalizationService>(sp => new LocalizationService(
            translationRegistry.Sources,
            sp.GetRequiredService<ILoggerService>(),
            "en"));

        // 4. Configure Modules
        var registrar = new ServiceRegistrar(services);
        foreach (var module in modules)
        {
            module.ConfigureServices(registrar);
        }

        var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<HardwareDetector>().Initialize();

        // 5. Load saved or default language
        _ = LoadSavedLanguageAsync(serviceProvider);

        // 6. Initialize AI Model Registry and set default server path
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

        var logger = serviceProvider.GetRequiredService<ILoggerService>();
        var modelRegistry = serviceProvider.GetRequiredService<IAIModelRegistry>();
        _ = modelRegistry.RefreshAsync();
        var skillRegistry = serviceProvider.GetRequiredService<ISkillRegistry>();
        try
        {
            skillRegistry.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.Warning("Bootstrapper", $"Failed to load skill registry: {ex.Message}");
        }

        // 7. Auto-start manager (mini orchestration) model server
        var serverManager = serviceProvider.GetRequiredService<LlamaCppServerManager>();
        
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait a moment for model registry to complete
                await Task.Delay(500);
                
                var models = await modelRegistry.GetAvailableModelsAsync();
                var managerModel = models.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Manager);
                
                if (managerModel != null)
                {
                    logger.Info("Bootstrapper", "Auto-starting manager (orchestration) model server...");
                    await serverManager.EnsureRunningAsync(managerModel, CancellationToken.None);
                    logger.Info("Bootstrapper", "Manager model server started successfully.");
                }
                else
                {
                    logger.Warning("Bootstrapper", "No manager model found. Orchestration auto-start skipped.");
                }
            }
            catch (Exception ex)
            {
                logger.Error("Bootstrapper", "Failed to auto-start manager model", ex);
            }
        });

        // 8. Register Routes, Sidebar Items, Tools and Widgets
        var navRegistry = serviceProvider.GetRequiredService<INavigationRegistry>();
        var funcRegistry = serviceProvider.GetRequiredService<IFunctionRegistry>();
        var sidebarService = serviceProvider.GetRequiredService<ISidebarService>();
        var widgetRegistry = serviceProvider.GetRequiredService<IWidgetRegistry>();

        foreach (var module in modules)
        {
            module.RegisterRoutes(navRegistry);
            module.RegisterSidebarItems(sidebarService);
            module.RegisterTools(funcRegistry, serviceProvider);
            module.RegisterWidgets(widgetRegistry);
        }

        ToolManifestValidator.ValidateAndLog(skillRegistry, funcRegistry, logger);

        return serviceProvider;
    }

    private static async Task LoadSavedLanguageAsync(IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<ISettingsService>();
        var loc = serviceProvider.GetRequiredService<ILocalizationService>();
        var savedLanguage = await settings.GetAsync<string>("App.Language", "en").ConfigureAwait(false);
        var languageToLoad = !string.IsNullOrWhiteSpace(savedLanguage) ? savedLanguage : "en";
        await loc.SetLanguageAsync(languageToLoad).ConfigureAwait(false);
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


