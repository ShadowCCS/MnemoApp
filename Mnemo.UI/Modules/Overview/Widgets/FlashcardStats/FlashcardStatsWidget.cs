using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Overview.Widgets;

namespace Mnemo.UI.Modules.Overview.Widgets.FlashcardStats;

/// <summary>
/// Factory for the Flashcard Statistics widget. Captures the services it needs at construction
/// time so each <see cref="CreateViewModel"/> call can hand them to a freshly built ViewModel.
/// </summary>
public class FlashcardStatsWidget : IWidget
{
    public WidgetMetadata Metadata { get; }
    private readonly IStatisticsManager _statistics;
    private readonly ILoggerService _logger;

    public FlashcardStatsWidget(IStatisticsManager statistics, ILoggerService logger)
    {
        _statistics = statistics;
        _logger = logger;
        Metadata = new WidgetMetadata(
            id: "flashcard-stats",
            title: "Flashcard Stats",
            description: "View your flashcard practice statistics and study streak",
            category: WidgetCategory.Statistics,
            icon: WidgetIconAvares.Uri("FlashcardStats"),
            defaultSize: new WidgetSize(colSpan: 2, rowSpan: 2),
            translationNamespace: "FlashcardStats",
            galleryFilter: WidgetGalleryFilterCategory.Study,
            galleryTagKeys: ["TagStats", "TagStreak"],
            isFeatured: true);
    }

    public IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null)
    {
        return new FlashcardStatsWidgetViewModel(_statistics, _logger);
    }
}
