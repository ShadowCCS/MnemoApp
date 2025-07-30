
using MnemoApp.Core.Navigation;

namespace MnemoApp.Core.MnemoAPI
{
    public interface IMnemoAPI
    {
        INavigationService navigate { get; set; }
    }
}