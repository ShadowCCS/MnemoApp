
using MnemoApp.Core.Navigation;

namespace MnemoApp.Core.MnemoAPI
{
    public class MnemoAPI : IMnemoAPI
    {
        public required INavigationService navigate { get; set; }

        public MnemoAPI(INavigationService navigationService)
        {
            navigate = navigationService;
        }

        //TODO: Add other services here
    }
}