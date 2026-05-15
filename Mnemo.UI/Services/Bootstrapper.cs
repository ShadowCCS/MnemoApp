using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services;
using Mnemo.Infrastructure.Services.Updates;

using Mnemo.Core.History;
using Mnemo.Infrastructure.History;
using Mnemo.Infrastructure.Services.AI;
using Mnemo.Infrastructure.Services.AI.PlatformHardware;
using Mnemo.Infrastructure.Services.Knowledge;
using Mnemo.Infrastructure.Services.Notes;
using Mnemo.Infrastructure.Services.Notes.Pdf;
using Mnemo.Infrastructure.Services.Flashcards;
using Mnemo.Infrastructure.Services.Speech;
using Mnemo.Infrastructure.Services.Statistics;
using Mnemo.Infrastructure.Services.TextShortcuts;
using Mnemo.Infrastructure.Services.Keybinds;
using Mnemo.Infrastructure.Services.Tools;
using Mnemo.Infrastructure.Services.Packaging;
using Mnemo.Infrastructure.Services.Packaging.PayloadHandlers;
using Mnemo.Infrastructure.Services.ImportExport;
using Mnemo.Infrastructure.Services.ImportExport.Adapters;
using Mnemo.Infrastructure.Services.Spellcheck;
using Mnemo.Infrastructure.Services.Search;
using Mnemo.Core.Services.Search;
using Mnemo.UI.Modules.Notes.Services;

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
        services.AddSingleton<IPerfDiagnostics, PerfDiagnosticsService>();
        services.AddSingleton<IUpdateService, VelopackUpdateService>();
        services.AddSingleton<IChatDatasetLogger, ChatDatasetLogger>();
        services.AddSingleton<DatasetExporter>();
        services.AddSingleton<ILaTeXEngine, LaTeXEngine>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<ITextMateSyntaxHighlighter, TextMateSyntaxHighlighter>();
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
        services.AddSingleton<INoteClipboardPayloadCodec, NoteClipboardPayloadCodec>();
        services.AddSingleton<INoteClipboardPlatformService, NoteClipboardPlatformService>();
        services.AddSingleton<IImageAssetService, ImageAssetService>();
        services.AddSingleton<ITextShortcutService, TextShortcutService>();
        services.AddSingleton<ISpellDictionaryCatalogService, SpellDictionaryCatalogService>();
        services.AddSingleton<IUserSpellbookService, UserSpellbookService>();
        services.AddSingleton<ISpellcheckService, HunspellSpellcheckService>();

        // AI Services
        services.AddSingleton<IPlatformHardwareGpuProvider>(sp =>
            PlatformHardwareGpuProviderFactory.Create(sp.GetRequiredService<ILoggerService>()));
        services.AddSingleton<HardwareDetector>();
        services.AddSingleton<IAIModelRegistry, ModelRegistry>();
        services.AddSingleton<IAIModelsSetupService, AIModelsSetupService>();
        services.AddSingleton<IAIModelInstallCoordinator, AIModelInstallCoordinator>();
        services.AddSingleton<IAiSetupOverlayPresenter, AiSetupOverlayPresenter>();
        services.AddSingleton<IResourceGovernor, ResourceGovernor>();
        services.AddSingleton<LlamaCppServerManager>();
        services.AddSingleton<IAIServerManager>(sp => sp.GetRequiredService<LlamaCppServerManager>());
        services.AddSingleton<LlamaCppHttpTextService>(sp =>
            new LlamaCppHttpTextService(
                sp.GetRequiredService<ILoggerService>(),
                new Lazy<LlamaCppServerManager>(() => sp.GetRequiredService<LlamaCppServerManager>())));
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
        services.AddSingleton<INotePdfLatexImageRenderer, NotePdfLatexImageRenderer>();
        services.AddSingleton<INotePdfExportService, NotePdfExportService>();
        services.AddSingleton(FlashcardFsrsParameters.Default);
        services.AddSingleton<IFlashcardScheduler, FsrsFlashcardScheduler>();
        services.AddSingleton<IFlashcardScheduler, Sm2FlashcardScheduler>();
        services.AddSingleton<IFlashcardScheduler, LeitnerFlashcardScheduler>();
        services.AddSingleton<IFlashcardScheduler, BaselineFlashcardScheduler>();
        services.AddSingleton<IFlashcardSchedulerResolver, FlashcardSchedulerResolver>();
        services.AddSingleton<IFlashcardDeckService, PersistentFlashcardDeckService>();
        services.AddSingleton<ISpeechRecognitionService, WhisperSpeechRecognitionService>();
        services.AddSingleton<IMnemoPackageService, MnemoPackageService>();
        services.AddSingleton<IMnemoPayloadHandler, NotesMnemoPayloadHandler>();
        services.AddSingleton<IMnemoPayloadHandler, SettingsMnemoPayloadHandler>();
        services.AddSingleton<IMnemoPayloadHandler, MindmapsMnemoPayloadHandler>();
        services.AddSingleton<IMnemoPayloadHandler, FlashcardsMnemoPayloadHandler>();
        services.AddSingleton<IImportExportCoordinator, ImportExportCoordinator>();
        services.AddSingleton<IContentFormatAdapter, NotesMnemoFormatAdapter>();
        services.AddSingleton<IContentFormatAdapter, NotesMarkdownFormatAdapter>();
        services.AddSingleton<IContentFormatAdapter, FlashcardsMnemoFormatAdapter>();
        services.AddSingleton<IContentFormatAdapter, FlashcardsCsvFormatAdapter>();
        services.AddSingleton<IContentFormatAdapter, FlashcardsAnkiFormatAdapter>();
        services.AddSingleton<IContentFormatAdapter, MindmapsMnemoFormatAdapter>();

        // 2. Register UI-specific Services
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IOverlayService, OverlayService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<IToastService>(sp => sp.GetRequiredService<ToastService>());
        services.AddSingleton<IUIService, UIService>();
        
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<INavigationRegistry>(sp => sp.GetRequiredService<NavigationService>());
        
        services.AddSingleton<SidebarService>();
        services.AddSingleton<ISidebarService>(sp => sp.GetRequiredService<SidebarService>());
        
        services.AddSingleton<IFunctionRegistry, FunctionRegistry>();
        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();

        // Statistics manager is shared by built-in modules, widgets, and extension tools.
        services.AddSingleton<IStatisticsManager, StatisticsManager>();
        services.AddSingleton<StatisticsToolService>();
        services.AddSingleton<NavigationStatisticsTracker>();
        services.AddSingleton<IGlobalSearchService, GlobalSearchService>();
        services.AddSingleton<ISearchProvider, NavigationSearchProvider>();

        // 3. Discover modules and register translation sources (before building provider)
        var discoverSw = Stopwatch.StartNew();
        var modules = DiscoverModules().ToList();
        var discoverMs = discoverSw.ElapsedMilliseconds;
        services.AddSingleton<IReadOnlyList<IModule>>(modules);
        services.AddSingleton<IAiAssistantToolHost, AiAssistantToolHost>();
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

        var keybindManifestCollector = new KeybindManifestCollector();
        foreach (var module in modules)
        {
            module.RegisterKeybindManifest(keybindManifestCollector);
        }

        services.AddSingleton<IKeybindRepository, SqliteKeybindRepository>();
        services.AddSingleton<IKeyMap>(sp => new KeyMapService(
            sp.GetRequiredService<IKeybindRepository>(),
            sp.GetRequiredService<ILoggerService>(),
            keybindManifestCollector.GetAll()));
        services.AddSingleton<IKeybindActionRouter, KeybindActionRouter>();
        services.AddSingleton<IEditorKeybindDispatch, EditorKeybindDispatch>();
        services.AddSingleton<IBlockEditorClipboardKeybindDispatch, BlockEditorClipboardKeybindDispatch>();
        services.AddSingleton<IMindmapKeybindDispatch, MindmapKeybindDispatch>();
        services.AddSingleton<IFlashcardDeckKeybindContext, FlashcardDeckKeybindContext>();
        services.AddSingleton<IFlashcardKeybindDispatch, FlashcardKeybindDispatch>();

        var buildSpSw = Stopwatch.StartNew();
        var serviceProvider = services.BuildServiceProvider();
        var buildSpMs = buildSpSw.ElapsedMilliseconds;

        var logger = serviceProvider.GetRequiredService<ILoggerService>();
        var perf = serviceProvider.GetRequiredService<IPerfDiagnostics>();
        perf.RecordTiming("Startup", "DiscoverModules", discoverMs);
        perf.RecordTiming("Startup", "BuildServiceProvider", buildSpMs);
        using (perf.Measure("Startup", "Bootstrapper.pre-window"))
            perf.CaptureMemorySnapshot("after BuildServiceProvider");

        // Welcome-note seed: runs once on fresh install, fire-and-forget so DB I/O doesn't block startup.
        _ = Task.Run(async () =>
        {
            try
            {
                await WelcomeNoteFirstRunSeed.TrySeedIfNeededAsync(
                    serviceProvider.GetRequiredService<INoteService>(),
                    serviceProvider.GetRequiredService<IStorageProvider>(),
                    logger).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error("Bootstrapper", "Welcome note seed failed.", ex);
            }
        });

        // GPU detection can call DXGI/WMI; cache it in the background so first AI request reuses it.
        _ = Task.Run(() => serviceProvider.GetRequiredService<HardwareDetector>().Initialize());

        // 5. Load saved or default language
        _ = LoadSavedLanguageAsync(serviceProvider);

        // 6. Initialize AI Model Registry and set default server path
        var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _ = Task.Run(async () =>
        {
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

        // Tools + skill manifests load only when AI.EnableAssistant is on (see AiAssistantToolHost).
        _ = serviceProvider.GetRequiredService<IAiAssistantToolHost>();

        // Local llama-server processes start on first generation route (LlamaCppHttpTextService / orchestration).

        // 7. Register Routes, Sidebar Items, and Widgets (tools deferred to AiAssistantToolHost)
        var navRegistry = serviceProvider.GetRequiredService<INavigationRegistry>();
        var sidebarService = serviceProvider.GetRequiredService<ISidebarService>();
        var widgetRegistry = serviceProvider.GetRequiredService<IWidgetRegistry>();

        foreach (var module in modules)
        {
            var moduleName = module.GetType().Name;
            var regSw = Stopwatch.StartNew();
            module.RegisterRoutes(navRegistry);
            var routesMs = regSw.ElapsedMilliseconds;
            perf.RecordTiming("Startup", $"{moduleName}.RegisterRoutes", routesMs);

            regSw.Restart();
            module.RegisterSidebarItems(sidebarService);
            var sidebarMs = regSw.ElapsedMilliseconds;
            perf.RecordTiming("Startup", $"{moduleName}.RegisterSidebarItems", sidebarMs);

            regSw.Restart();
            module.RegisterWidgets(widgetRegistry, serviceProvider);
            var widgetsMs = regSw.ElapsedMilliseconds;
            perf.RecordTiming("Startup", $"{moduleName}.RegisterWidgets", widgetsMs);
        }

        perf.CaptureMemorySnapshot("after module registration");

        _ = serviceProvider.GetRequiredService<NavigationStatisticsTracker>();

        // Launch stats: write to SQLite in background, not on the hot startup path.
        _ = StatisticsRecorder.RecordAppLaunchAsync(
            serviceProvider.GetRequiredService<IStatisticsManager>(), logger);

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
        // Scan Mnemo.* assemblies only; plugin assemblies can be loaded into the AppDomain before discovery.
        var assemblySet = new HashSet<Assembly>();
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.FullName?.StartsWith("Mnemo.", StringComparison.Ordinal) == true)
                assemblySet.Add(a);
        }

        // Startup can run before every Mnemo.* assembly is in the AppDomain; UI modules always live here.
        assemblySet.Add(typeof(Bootstrapper).Assembly);

        var moduleType = typeof(IModule);
        var foundModules = new List<IModule>(32);

        foreach (var assembly in assemblySet)
        {
            Type[] types;
            try
            {
                // GetExportedTypes is cheaper than GetTypes (skips internal/nested-private; all IModule impls are public).
                types = assembly.GetExportedTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.IsInterface || type.IsAbstract) continue;
                if (!moduleType.IsAssignableFrom(type)) continue;

                try
                {
                    if (Activator.CreateInstance(type) is IModule module)
                        foundModules.Add(module);
                }
                catch
                {
                    // Module instantiation failures are ignored during discovery phase.
                }
            }
        }
        return foundModules;
    }
}


