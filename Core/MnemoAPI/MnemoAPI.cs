
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;

namespace MnemoApp.Core.MnemoAPI
{
    public class MnemoAPI : IMnemoAPI
    {
        public required INavigationService navigate { get; set; }
        public required ISidebarService sidebar { get; set; }

        public MnemoAPI(INavigationService navigationService, ISidebarService sidebarService)
        {
            navigate = navigationService;
            sidebar = sidebarService;
        }

        //TODO: Add other services here
    }
}