using Mnemo.Core.Services;
using Mnemo.UI.Modules.Settings.ViewModels;

namespace Mnemo.UI.Modules.Settings;

public class SettingsModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<SettingsViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("settings", typeof(SettingsViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Settings", "settings", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/settings.svg", "Ecosystem", 2);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // No tools for settings yet
    }
}

