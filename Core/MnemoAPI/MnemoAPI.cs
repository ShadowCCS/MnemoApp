
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Services;

namespace MnemoApp.Core.MnemoAPI
{
    public class MnemoAPI : IMnemoAPI
    {
        public required INavigationService navigate { get; set; }
        public required ISidebarService sidebar { get; set; }
        public required IOverlayService overlay { get; set; }

        public MnemoAPI(INavigationService navigationService, ISidebarService sidebarService, IOverlayService overlayService)
        {
            navigate = navigationService;
            sidebar = sidebarService;
            overlay = overlayService;
        }

        //TODO: Add other services here
    }
}