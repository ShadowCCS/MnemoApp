using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Overview.Widgets;

namespace Mnemo.UI.Modules.Overview.Widgets.UsageSummary;

public sealed class UsageSummaryWidget : IWidget
{
    public WidgetMetadata Metadata { get; }
    private readonly IStatisticsManager _statistics;
    private readonly ILoggerService _logger;

    public UsageSummaryWidget(IStatisticsManager statistics, ILoggerService logger)
    {
        _statistics = statistics;
        _logger = logger;
        Metadata = new WidgetMetadata(
            id: "usage-summary",
            title: "Usage",
            description: "Launches, notes created, and time on screen today",
            category: WidgetCategory.Statistics,
            icon: WidgetIconAvares.Uri("UsageSummary"),
            defaultSize: new WidgetSize(colSpan: 2, rowSpan: 2),
            translationNamespace: "UsageSummary",
            galleryFilter: WidgetGalleryFilterCategory.Study,
            galleryTagKeys: ["TagUsage", "TagStats"]);
    }

    public IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null)
        => new UsageSummaryWidgetViewModel(_statistics, _logger);
}
