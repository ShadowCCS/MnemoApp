using System;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Keybinds;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Infrastructure.Services.Tools;
using Mnemo.UI.Components;
using Mnemo.UI.Components.RightSidebar;
using Mnemo.UI.Components.Sidebar;
using Mnemo.UI.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules;

public class CoreUIModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddSingleton<ChatPauseToSendEstimator>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SidebarViewModel>();
        services.AddTransient<RightSidebarViewModel>();
        services.AddTransient<TopbarViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
        var app = services.GetRequiredService<ApplicationToolService>();
        ApplicationToolRegistrar.Register(registry, app);
        SkillDiscoveryToolRegistrar.Register(registry, services.GetRequiredService<SkillDiscoveryToolService>());
    }

    public void RegisterWidgets(IWidgetRegistry registry, IServiceProvider services)
    {
    }

    public void RegisterKeybindManifest(IKeybindManifestRegistry registry)
    {
        registry.Register(new KeybindActionDefinition
        {
            ActionId = "global.search",
            Namespace = "global",
            Scope = KeybindScope.Global,
            Bindings =
            [
                new KeybindBindingEntry
                {
                    Kind = KeybindBindingKind.Chord,
                    Chord = CanonicalKeyGestureCodec.ParseChord("Primary+K")
                }
            ]
        });
    }
}
