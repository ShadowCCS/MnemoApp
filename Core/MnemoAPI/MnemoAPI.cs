
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Storage;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.Tasks.Services;
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
        public required AIApi ai { get; set; }
        public required TaskApi tasks { get; set; }

        public MnemoAPI(
            INavigationService navigationService, 
            ISidebarService sidebarService, 
            IThemeService themeService, 
            ITopbarService topbarService, 
            IOverlayService overlayService, 
            IAIService aiService, 
            IModelSelectionService modelSelectionService, 
            ITaskSchedulerService taskSchedulerService,
            MnemoDataApi dataApi,
            MnemoStorageManager storageManager,
            ILocalizationService localizationService,
            IToastService toastService,
            IDropdownItemRegistry dropdownRegistry)
        {
            navigate = navigationService;
            sidebar = sidebarService;
            ui = new UIApi(themeService, topbarService, overlayService, localizationService, toastService, dropdownRegistry, taskSchedulerService);
            system = new SystemApi();
            ai = new AIApi(aiService, modelSelectionService);
            tasks = new TaskApi(taskSchedulerService, aiService, modelSelectionService);
            data = dataApi;
            storage = storageManager;
        }
    }
}