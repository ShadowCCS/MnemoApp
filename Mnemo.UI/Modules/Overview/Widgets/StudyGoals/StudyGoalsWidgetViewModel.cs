using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Widgets.StudyGoals;

/// <summary>
/// Represents a single study goal item.
/// </summary>
public partial class StudyGoalItem : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private int _target;

    [ObservableProperty]
    private int _completed;

    public string ProgressText => $"{Completed}/{Target}";
}

/// <summary>
/// ViewModel for the Study Goals widget.
/// Displays daily study goals and progress.
/// </summary>
public partial class StudyGoalsWidgetViewModel : WidgetViewModelBase
{
    public ObservableCollection<StudyGoalItem> Goals { get; } = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // TODO: Load actual data from services
        Goals.Clear();
        Goals.Add(new StudyGoalItem { Title = "Cards to review", Target = 50, Completed = 32 });
        Goals.Add(new StudyGoalItem { Title = "New cards", Target = 20, Completed = 12 });
        Goals.Add(new StudyGoalItem { Title = "Minutes studied", Target = 30, Completed = 25 });
    }
}
