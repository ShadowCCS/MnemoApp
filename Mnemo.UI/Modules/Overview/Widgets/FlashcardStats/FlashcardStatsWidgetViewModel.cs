using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Widgets.FlashcardStats;

/// <summary>
/// ViewModel for the Flashcard Statistics widget. Reads the lifetime totals + today's daily
/// summary from <see cref="IStatisticsManager"/>; falls back to zero values when the user
/// has not yet practiced (empty state, never throws).
/// </summary>
public partial class FlashcardStatsWidgetViewModel : WidgetViewModelBase
{
    private readonly IStatisticsManager _statistics;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private int _totalCardsPracticed;

    [ObservableProperty]
    private int _studyStreak;

    [ObservableProperty]
    private int _cardsToday;

    public FlashcardStatsWidgetViewModel(IStatisticsManager statistics, ILoggerService logger)
    {
        _statistics = statistics;
        _logger = logger;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        try
        {
            var totals = (await _statistics.GetAsync(
                StatisticsNamespaces.Flashcards,
                FlashcardStatKinds.LifetimeTotals,
                "all").ConfigureAwait(false)).Value;

            TotalCardsPracticed = (int)Math.Min(int.MaxValue, ReadInt(totals, "total_cards_practiced"));
            StudyStreak = (int)Math.Min(int.MaxValue, ReadInt(totals, "current_streak_days"));

            var dayKey = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var today = (await _statistics.GetAsync(
                StatisticsNamespaces.Flashcards,
                FlashcardStatKinds.DailySummary,
                dayKey).ConfigureAwait(false)).Value;
            CardsToday = (int)Math.Min(int.MaxValue, ReadInt(today, "cards_reviewed"));
        }
        catch (Exception ex)
        {
            _logger?.Error("Overview", "Loading flashcard stats widget failed.", ex);
            TotalCardsPracticed = 0;
            StudyStreak = 0;
            CardsToday = 0;
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
