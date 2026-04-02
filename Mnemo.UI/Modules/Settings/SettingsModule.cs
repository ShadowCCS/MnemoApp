using System;
using Mnemo.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Infrastructure.Services.Tools;
using Mnemo.UI.Modules.Settings.ViewModels;

namespace Mnemo.UI.Modules.Settings;

public class SettingsModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<SettingsViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("settings", typeof(SettingsViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Settings", "settings", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/settings.svg", "Ecosystem", 2, int.MaxValue);
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
        var svc = services.GetRequiredService<SettingsToolService>();
        SettingsToolRegistrar.Register(registry, svc);
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
    }
}
