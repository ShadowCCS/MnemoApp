
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Storage;
using MnemoApp.Data.Runtime;
using MnemoApp.Data.Packaged;

namespace MnemoApp.Core.MnemoAPI
{
    public class MnemoAPI : IMnemoAPI
    {
        public required INavigationService navigate { get; set; }
        public required ISidebarService sidebar { get; set; }
        public required UIApi ui { get; set; }
        public required SystemApi system { get; set; }
        public required MnemoDataApi data { get; set; }
        public required MnemoApp.Data.Packaged.MnemoStorageManager storage { get; set; }

        public MnemoAPI(INavigationService navigationService, ISidebarService sidebarService, IThemeService themeService, ITopbarService topbarService, IOverlayService overlayService)
        {
            navigate = navigationService;
            sidebar = sidebarService;
            ui = new UIApi(themeService, topbarService, overlayService);
            system = new SystemApi();
            // Resolve storage via DI singletons
            var runtimeStorage = Core.ApplicationHost.Services.GetService(typeof(MnemoApp.Data.Runtime.IRuntimeStorage)) as MnemoApp.Data.Runtime.IRuntimeStorage;
            storage = (Core.ApplicationHost.Services.GetService(typeof(MnemoApp.Data.Packaged.MnemoStorageManager)) as MnemoApp.Data.Packaged.MnemoStorageManager) ?? new MnemoApp.Data.Packaged.MnemoStorageManager();
            data = new MnemoDataApi(runtimeStorage ?? new SqliteRuntimeStorage(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "MnemoApp", "Runtime")), storage);
        }

        //TODO: Add other services here
    }
}