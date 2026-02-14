using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;

namespace Mnemo.UI.Modules.Overview.Widgets.FlashcardStats;

/// <summary>
/// Factory for the Flashcard Statistics widget.
/// </summary>
public class FlashcardStatsWidget : IWidget
{
    public WidgetMetadata Metadata { get; }

    public FlashcardStatsWidget()
    {
        Metadata = new WidgetMetadata(
            id: "flashcard-stats",
            title: "Flashcard Stats",
            description: "View your flashcard practice statistics and study streak",
            category: WidgetCategory.Statistics,
            icon: "avares://Mnemo.UI/Icons/Tabler/Used/Filled/cards.svg",
            defaultSize: new WidgetSize(colSpan: 2, rowSpan: 2)
        );
    }

    public IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null)
    {
        return new FlashcardStatsWidgetViewModel();
    }
}
