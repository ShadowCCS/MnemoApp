using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core.Navigation;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Services;
using MnemoApp.Core.Shell;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.Tasks.Services;
using MnemoApp.UI.Components.Sidebar;
using MnemoApp.Modules.Dashboard;
using MnemoApp.Modules.Paths;
using MnemoApp.Modules.Settings;
using MnemoApp.Modules.TestModule;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Storage;
using MnemoApp.Data.Runtime;
using MnemoApp.Data.Packaged;

namespace MnemoApp.Core
{
    public class ApplicationHost
    {
        private static IServiceProvider? _serviceProvider;

        // Internal accessor for App.axaml.cs only - do not use elsewhere
        internal static IServiceProvider GetServiceProvider() 
            => _serviceProvider ?? throw new InvalidOperationException("Host not initialized");

        public static async Task InitializeAsync()
        {
            if (_serviceProvider != null)
                return; // Already initialized
                
            var services = new ServiceCollection();

            // Register core services
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ISidebarService, SidebarService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<ITopbarService, TopbarService>();
            services.AddSingleton<IToastService, ToastService>();
            services.AddSingleton<IOverlayService, OverlayManager>();
            services.AddSingleton<IDropdownItemRegistry, DropdownItemRegistry>();
            services.AddSingleton<IAIService, AIService>();
            services.AddSingleton<IModelSelectionService, ModelSelectionService>();
            services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();
            
            // Storage
            services.AddSingleton<IRuntimeStorage>(sp =>
            {
                var baseDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "MnemoApp", "Runtime");
                return new SqliteRuntimeStorage(baseDir);
            });
            services.AddSingleton<MnemoStorageManager>();
            services.AddSingleton<MnemoDataApi>(sp =>
            {
                var runtime = sp.GetRequiredService<IRuntimeStorage>();
                var packaged = sp.GetRequiredService<MnemoStorageManager>();
                return new MnemoDataApi(runtime, packaged);
            });
            
            // Register MnemoAPI
            services.AddSingleton<IMnemoAPI, MnemoApp.Core.MnemoAPI.MnemoAPI>();

            // Register ViewModels and Views
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<SidebarViewModel>();
            
            // Register modules
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<PathsViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<TestModuleViewModel>();

            _serviceProvider = services.BuildServiceProvider();

            // Initialize localization first and await it fully
            var localization = _serviceProvider.GetRequiredService<ILocalizationService>();
            await localization.InitializeAsync();

            // Initialize AI services in background to avoid blocking UI startup
            var aiService = _serviceProvider.GetRequiredService<IAIService>();
            _ = Task.Run(async () =>
            {
                try { await aiService.InitializeAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AI init failed: {ex.Message}"); }
            });

            var modelSelectionService = _serviceProvider.GetRequiredService<IModelSelectionService>();
            _ = Task.Run(async () =>
            {
                try { await modelSelectionService.InitializeAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ModelSelection init failed: {ex.Message}"); }
            });

            // Initialize and start task scheduler
            var taskScheduler = _serviceProvider.GetRequiredService<ITaskSchedulerService>();
            await taskScheduler.StartAsync();

            // Initialize MnemoAPI
            var api = _serviceProvider.GetRequiredService<IMnemoAPI>();
            
            // Load saved language preference and apply it
            try
            {
                var savedLanguage = api.data.GetProperty<string>("Language");
                if (!string.IsNullOrWhiteSpace(savedLanguage))
                {
                    await localization.SetLanguageAsync(savedLanguage);
                }
            }
            catch { /* ignore */ }
            
            // Rebuild sidebar when language changes and save language preference
            var sidebar = _serviceProvider.GetRequiredService<ISidebarService>();
            localization.LanguageChanged += (sender, code) =>
            {
                // Save language preference to storage
                api.data.SetProperty("Language", code);
                
                sidebar.ClearAll();
                RegisterModulesWithSidebar(api);
            };

            // Initial sidebar setup - now happens after localization is fully ready
            RegisterModulesWithSidebar(api);
        }
        
        private static void RegisterModulesWithSidebar(IMnemoAPI api)
        {
            // Register Dashboard module
            api.sidebar.Register(
                api.ui.language.get("Dashboard", "Title"),
                typeof(DashboardViewModel),
                api.ui.language.get("Sidebar", "Main Hub"),
                "avares://MnemoApp/UI/Icons/Tabler/outline/home.svg"
            );
            
            // Register Learning modules
            api.sidebar.Register(
                api.ui.language.get("Paths", "Learning Paths"),
                typeof(PathsViewModel),
                api.ui.language.get("Sidebar", "Main Hub"),
                "avares://MnemoApp/UI/Icons/Tabler/outline/route-alt-left.svg"
            );
            
            // Register child ViewModels for Learning Paths
            var pathsItem = api.sidebar.GetItem(api.ui.language.get("Paths", "Learning Paths"), api.ui.language.get("Sidebar", "Main Hub"));
            if (pathsItem != null)
            {
                pathsItem.ChildViewModelTypes.Add(typeof(Modules.Paths.UnitOverview.UnitOverviewViewModel));
                pathsItem.ChildViewModelTypes.Add(typeof(Modules.Paths.UnitView.UnitViewViewModel));
            }

            // Register Test Module
            api.sidebar.Register(
                api.ui.language.get("TestModule", "Title"),
                typeof(TestModuleViewModel),
                api.ui.language.get("Sidebar", "Main Hub"),
                "avares://MnemoApp/UI/Icons/Tabler/outline/test-pipe.svg"
            );

            // Register Settings module
            api.sidebar.Register(
                api.ui.language.get("Settings", "Title"),
                typeof(SettingsViewModel),
                api.ui.language.get("Sidebar", "Utility & Personalization"),
                "avares://MnemoApp/UI/Icons/Tabler/outline/adjustments-alt.svg"
            );
        }

        /// <summary>
        /// Clean shutdown of all services, especially AI services
        /// </summary>
        public static async ValueTask ShutdownAsync()
        {
            if (_serviceProvider != null)
            {
                try
                {
                    // Stop task scheduler first
                    var taskScheduler = _serviceProvider.GetService<ITaskSchedulerService>();
                    if (taskScheduler != null)
                    {
                        await taskScheduler.StopAsync();
                    }

                    // Dispose AI services first to ensure clean process termination
                    var aiService = _serviceProvider.GetService<IAIService>();
                    if (aiService is IAsyncDisposable aiDisposable)
                    {
                        await aiDisposable.DisposeAsync();
                    }

                    // Dispose storage services
                    var runtimeStorage = _serviceProvider.GetService<IRuntimeStorage>();
                    if (runtimeStorage is IAsyncDisposable storageDisposable)
                    {
                        await storageDisposable.DisposeAsync();
                    }
                    else if (runtimeStorage is IDisposable storageSync)
                    {
                        storageSync.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during service shutdown: {ex.Message}");
                }
                finally
                {
                    // Always dispose the service provider
                    if (_serviceProvider is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync();
                    }
                    else if (_serviceProvider is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    
                    _serviceProvider = null;
                }
            }
        }
        
    }
}
