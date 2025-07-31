
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;

namespace MnemoApp.Core.MnemoAPI
{
    public interface IMnemoAPI
    {
        INavigationService navigate { get; set; }
        ISidebarService sidebar { get; set; }
    }
}