using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Overview.Widgets;

namespace Mnemo.UI.Modules.Overview.Widgets.StudyGoals;

/// <summary>
/// Factory for the Study Goals widget. Pulls today's daily-summary record from the statistics
/// manager so the goals reflect actual practice activity for the current UTC day.
/// </summary>
public class StudyGoalsWidget : IWidget
{
    public WidgetMetadata Metadata { get; }
    private readonly IStatisticsManager _statistics;
    private readonly ILoggerService _logger;

    public StudyGoalsWidget(IStatisticsManager statistics, ILoggerService logger)
    {
        _statistics = statistics;
        _logger = logger;
        Metadata = new WidgetMetadata(
            id: "study-goals",
            title: "Study Goals",
            description: "Track your daily study goals and progress",
            category: WidgetCategory.Activity,
            icon: WidgetIconAvares.Uri("StudyGoals"),
            defaultSize: new WidgetSize(colSpan: 2, rowSpan: 2),
            translationNamespace: "StudyGoals");
    }

    public IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null)
    {
        return new StudyGoalsWidgetViewModel(_statistics, _logger);
    }
}
