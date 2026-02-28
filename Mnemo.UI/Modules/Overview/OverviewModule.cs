using System.Reflection;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview;

public class OverviewModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<OverviewViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
        var assembly = typeof(OverviewModule).Assembly;
        registry.Add(new EmbeddedJsonTranslationSource(assembly, "Mnemo.UI.Modules.Overview.Widgets.FlashcardStats.Translations"));
        registry.Add(new EmbeddedJsonTranslationSource(assembly, "Mnemo.UI.Modules.Overview.Widgets.RecentDecks.Translations"));
        registry.Add(new EmbeddedJsonTranslationSource(assembly, "Mnemo.UI.Modules.Overview.Widgets.StudyGoals.Translations"));
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("overview", typeof(OverviewViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Overview", "overview", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/home.svg", "MainHub", 0, 0);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // No tools for overview yet
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
        // Register the sample widgets
        registry.RegisterWidget(new Widgets.FlashcardStats.FlashcardStatsWidget());
        registry.RegisterWidget(new Widgets.RecentDecks.RecentDecksWidget());
        registry.RegisterWidget(new Widgets.StudyGoals.StudyGoalsWidget());
    }
}

