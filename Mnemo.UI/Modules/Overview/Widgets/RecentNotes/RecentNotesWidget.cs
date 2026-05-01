using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Overview.Widgets;

namespace Mnemo.UI.Modules.Overview.Widgets.RecentNotes;

public sealed class RecentNotesWidget : IWidget
{
    public WidgetMetadata Metadata { get; }
    private readonly INoteService _notes;
    private readonly INavigationService _navigation;
    private readonly ILoggerService _logger;

    public RecentNotesWidget(INoteService notes, INavigationService navigation, ILoggerService logger)
    {
        _notes = notes;
        _navigation = navigation;
        _logger = logger;
        Metadata = new WidgetMetadata(
            id: "recent-notes",
            title: "Recent notes",
            description: "Notes you edited most recently",
            category: WidgetCategory.Activity,
            icon: WidgetIconAvares.Uri("RecentNotes"),
            defaultSize: new WidgetSize(colSpan: 3, rowSpan: 2),
            translationNamespace: "RecentNotes");
    }

    public IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null)
        => new RecentNotesWidgetViewModel(_notes, _navigation, _logger);
}
