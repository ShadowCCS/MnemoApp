using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Services;
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
/// ViewModel for the Study Goals widget. Uses simple default targets; optional user goals could
/// later come from settings. The second row tracks completed flashcard sessions today (daily summary).
/// </summary>
public partial class StudyGoalsWidgetViewModel : WidgetViewModelBase
{
    private const int DefaultCardsTarget = 50;
    private const int DefaultSessionsTarget = 3;
    private const int DefaultMinutesTarget = 30;

    private readonly IStatisticsManager _statistics;
    private readonly ILoggerService _logger;

    public StudyGoalsWidgetViewModel(IStatisticsManager statistics, ILoggerService logger)
    {
        _statistics = statistics;
        _logger = logger;
    }

    public ObservableCollection<StudyGoalItem> Goals { get; } = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Goals.Clear();

        try
        {
            var dayKey = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var record = (await _statistics.GetAsync(
                StatisticsNamespaces.Flashcards,
                FlashcardStatKinds.DailySummary,
                dayKey).ConfigureAwait(false)).Value;

            var cardsReviewed = (int)Math.Min(int.MaxValue, ReadInt(record, "cards_reviewed"));
            var minutesStudied = (int)Math.Min(int.MaxValue, ReadInt(record, "minutes_studied"));
            var sessionsCompleted = (int)Math.Min(int.MaxValue, ReadInt(record, "sessions_completed"));

            Goals.Add(new StudyGoalItem
            {
                Title = "Cards reviewed",
                Target = DefaultCardsTarget,
                Completed = Math.Min(cardsReviewed, DefaultCardsTarget)
            });
            Goals.Add(new StudyGoalItem
            {
                Title = "Sessions completed",
                Target = DefaultSessionsTarget,
                Completed = Math.Min(sessionsCompleted, DefaultSessionsTarget)
            });
            Goals.Add(new StudyGoalItem
            {
                Title = "Minutes studied",
                Target = DefaultMinutesTarget,
                Completed = Math.Min(minutesStudied, DefaultMinutesTarget)
            });
        }
        catch (Exception ex)
        {
            _logger?.Error("Overview", "Loading study goals widget failed.", ex);

            Goals.Add(new StudyGoalItem { Title = "Cards reviewed", Target = DefaultCardsTarget, Completed = 0 });
            Goals.Add(new StudyGoalItem { Title = "Sessions completed", Target = DefaultSessionsTarget, Completed = 0 });
            Goals.Add(new StudyGoalItem { Title = "Minutes studied", Target = DefaultMinutesTarget, Completed = 0 });
        }
    }

    private static long ReadInt(StatisticsRecord? record, string field)
    {
        if (record == null) return 0L;
        return record.Fields.TryGetValue(field, out var v) && v.Type == StatValueType.Integer
            ? v.AsInt()
            : 0L;
    }
}
