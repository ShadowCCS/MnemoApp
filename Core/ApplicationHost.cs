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

            // Initialize localization first and await it fully
            var localization = _serviceProvider.GetRequiredService<ILocalizationService>();
            await localization.InitializeAsync();

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
            
            // Rebuild sidebar when language changes
            localization.LanguageChanged += (sender, code) =>
            {
                var sidebar = _serviceProvider.GetRequiredService<ISidebarService>();
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
                api.ui.language.get("Flashcards", "Title"),
                typeof(FlashcardsViewModel),
                api.ui.language.get("Sidebar", "Main Hub"),
                "avares://MnemoApp/UI/Icons/Tabler/outline/layout-cards.svg"
            );

            // Register Settings module
            api.sidebar.Register(
                api.ui.language.get("Settings", "Title"),
                typeof(SettingsViewModel),
                api.ui.language.get("Sidebar", "Utility & Personalization"),
                "avares://MnemoApp/UI/Icons/Tabler/outline/adjustments-alt.svg"
            );
        }
        
    }
}
