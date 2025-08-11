
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;

namespace MnemoApp.Core.MnemoAPI
{
    public class MnemoAPI : IMnemoAPI
    {
        public required INavigationService navigate { get; set; }
        public required ISidebarService sidebar { get; set; }
        public required UIApi ui { get; set; }
        public required SystemApi system { get; set; }

        public MnemoAPI(INavigationService navigationService, ISidebarService sidebarService, IThemeService themeService, ITopbarService topbarService)
        {
            navigate = navigationService;
            sidebar = sidebarService;
            ui = new UIApi(themeService, topbarService);
            system = new SystemApi();
        }

        //TODO: Add other services here
    }
}