using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Widgets.RecentDecks;

/// <summary>
/// Represents a recently practiced deck.
/// </summary>
public partial class RecentDeckItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _subject = string.Empty;

    [ObservableProperty]
    private int _cardCount;

    [ObservableProperty]
    private DateTime _lastPracticed;

    public string LastPracticedText => LastPracticed.ToString("MMM dd, yyyy");
}

/// <summary>
/// ViewModel for the Recent Decks widget.
/// Displays the user's recently practiced flashcard decks.
/// </summary>
public partial class RecentDecksWidgetViewModel : WidgetViewModelBase
{
    public ObservableCollection<RecentDeckItem> RecentDecks { get; } = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // TODO: Load actual data from services
        // For now, use placeholder data
        RecentDecks.Clear();
        RecentDecks.Add(new RecentDeckItem
        {
            Name = "Spanish Vocabulary",
            Subject = "Language",
            CardCount = 150,
            LastPracticed = DateTime.Now.AddHours(-2)
        });
        RecentDecks.Add(new RecentDeckItem
        {
            Name = "Biology: Cell Structure",
            Subject = "Science",
            CardCount = 85,
            LastPracticed = DateTime.Now.AddDays(-1)
        });
        RecentDecks.Add(new RecentDeckItem
        {
            Name = "World History",
            Subject = "History",
            CardCount = 200,
            LastPracticed = DateTime.Now.AddDays(-2)
        });
        RecentDecks.Add(new RecentDeckItem
        {
            Name = "Math Formulas",
            Subject = "Mathematics",
            CardCount = 64,
            LastPracticed = DateTime.Now.AddDays(-3)
        });
    }
}
