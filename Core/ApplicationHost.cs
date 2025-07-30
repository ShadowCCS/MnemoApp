using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core.Navigation;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Shell;

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
            
            // Register MnemoAPI
            services.AddSingleton<IMnemoAPI, MnemoApp.Core.MnemoAPI.MnemoAPI>();

            // Register MainWindow and ViewModels
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // Initialize MnemoAPI if needed
            var mnemoAPI = _serviceProvider.GetRequiredService<IMnemoAPI>();
            // mnemoAPI.Initialize(); // Uncomment if MnemoAPI has an Initialize method

            // TODO: Load modules/extensions here
        }
    }
}
