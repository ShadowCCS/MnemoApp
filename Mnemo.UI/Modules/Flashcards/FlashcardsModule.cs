using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services;
using Mnemo.UI.Modules.Flashcards.ViewModels;

namespace Mnemo.UI.Modules.Flashcards;

/// <summary>
/// Registers flashcard library, deck detail, and practice routes.
/// </summary>
public class FlashcardsModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<FlashcardsViewModel>();
        services.AddTransient<FlashcardDeckDetailViewModel>();
        services.AddTransient<FlashcardPracticeViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
        var assembly = typeof(FlashcardsModule).Assembly;
        registry.Add(new EmbeddedJsonTranslationSource(assembly, "Mnemo.UI.Modules.Flashcards.Translations"));
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("flashcards", typeof(FlashcardsViewModel));
        registry.RegisterRoute("flashcard-deck", typeof(FlashcardDeckDetailViewModel));
        registry.RegisterRoute("flashcard-practice", typeof(FlashcardPracticeViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem(
            "Flashcards",
            "flashcards",
            "avares://Mnemo.UI/Icons/Sidebar/flashcard.svg",
            "Library",
            1,
            40);
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
    }

    public void RegisterWidgets(IWidgetRegistry registry, IServiceProvider services)
    {
    }
}
