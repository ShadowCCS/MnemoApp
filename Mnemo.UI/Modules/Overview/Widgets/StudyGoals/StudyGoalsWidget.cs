using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;

namespace Mnemo.UI.Modules.Overview.Widgets.StudyGoals;

/// <summary>
/// Factory for the Study Goals widget.
/// </summary>
public class StudyGoalsWidget : IWidget
{
    public WidgetMetadata Metadata { get; }

    public StudyGoalsWidget()
    {
        Metadata = new WidgetMetadata(
            id: "study-goals",
            title: "Study Goals",
            description: "Track your daily study goals and progress",
            category: WidgetCategory.Activity,
            icon: "avares://Mnemo.UI/Icons/Tabler/Used/Filled/template.svg",
            defaultSize: new WidgetSize(colSpan: 2, rowSpan: 2)
        );
    }

    public IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null)
    {
        return new StudyGoalsWidgetViewModel();
    }
}
