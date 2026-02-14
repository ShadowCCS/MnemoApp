using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;

namespace Mnemo.UI.Modules.Overview.Widgets.RecentDecks;

/// <summary>
/// Factory for the Recent Decks widget.
/// </summary>
public class RecentDecksWidget : IWidget
{
    public WidgetMetadata Metadata { get; }

    public RecentDecksWidget()
    {
        Metadata = new WidgetMetadata(
            id: "recent-decks",
            title: "Recent Decks",
            description: "View your recently practiced flashcard decks",
            category: WidgetCategory.Activity,
            icon: "avares://Mnemo.UI/Icons/Tabler/Used/Filled/book.svg",
            defaultSize: new WidgetSize(colSpan: 3, rowSpan: 2)
        );
    }

    public IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null)
    {
        return new RecentDecksWidgetViewModel();
    }
}
