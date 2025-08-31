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
using MnemoApp.UI.Components.Sidebar;
using MnemoApp.Modules.Dashboard;
using MnemoApp.Modules.Learning;
using MnemoApp.Modules.Settings;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Storage;
using MnemoApp.Data.Runtime;
using MnemoApp.Data.Packaged;

namespace MnemoApp.Core
{
    public class ApplicationHost
    {
        private static IServiceProvider? _serviceProvider;

        public static IServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("Host not initialized");

        public static void Initialize()
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
            
            // Storage
            services.AddSingleton<IRuntimeStorage>(sp =>
            {
                var baseDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "MnemoApp", "Runtime");
                return new SqliteRuntimeStorage(baseDir);
            });
            services.AddSingleton<MnemoStorageManager>();
            
            // Register MnemoAPI
            services.AddSingleton<IMnemoAPI, MnemoApp.Core.MnemoAPI.MnemoAPI>();

            // Register ViewModels and Views
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<SidebarViewModel>();
            
            // Register modules
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<FlashcardsViewModel>();
            services.AddTransient<SettingsViewModel>();

            _serviceProvider = services.BuildServiceProvider();

            // Initialize MnemoAPI if needed
            var api = _serviceProvider.GetRequiredService<IMnemoAPI>();

            // Initialize localization early
            var loc = _serviceProvider.GetRequiredService<ILocalizationService>();
            _ = loc.InitializeAsync();

            // Register modules with sidebar
            RegisterModulesWithSidebar(api);
        }
        
        private static void RegisterModulesWithSidebar(IMnemoAPI api)
        {
            // Register Dashboard module
            api.sidebar.Register(
                "Dashboard", 
                typeof(DashboardViewModel), 
                "Main Hub", 
                "avares://MnemoApp/UI/Icons/Tabler/outline/home.svg"
            );
            
            // Register Learning modules
            api.sidebar.Register(
                "Flashcards",
                typeof(FlashcardsViewModel),
                "Main Hub",
                "avares://MnemoApp/UI/Icons/Tabler/outline/layout-cards.svg"
            );

            // Register Settings module
            api.sidebar.Register(
                "Settings", 
                typeof(SettingsViewModel), 
                "Utility & Personalization", 
                "avares://MnemoApp/UI/Icons/Tabler/outline/adjustments-alt.svg"
            );
        }
        
    }
}
