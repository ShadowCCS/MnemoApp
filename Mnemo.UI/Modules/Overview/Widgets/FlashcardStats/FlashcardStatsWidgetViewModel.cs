using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Widgets.FlashcardStats;

/// <summary>
/// ViewModel for the Flashcard Statistics widget.
/// Displays total flashcards practiced and study streak.
/// </summary>
public partial class FlashcardStatsWidgetViewModel : WidgetViewModelBase
{
    [ObservableProperty]
    private int _totalCardsPracticed;

    [ObservableProperty]
    private int _studyStreak;

    [ObservableProperty]
    private int _cardsToday;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // TODO: Load actual data from services
        // For now, use placeholder data
        TotalCardsPracticed = 1234;
        StudyStreak = 7;
        CardsToday = 42;
    }
}
