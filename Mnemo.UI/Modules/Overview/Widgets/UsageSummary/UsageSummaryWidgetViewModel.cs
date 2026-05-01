using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Widgets.UsageSummary;

public partial class UsageSummaryWidgetViewModel : WidgetViewModelBase
{
    private readonly IStatisticsManager _statistics;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private string _launchCountDisplay = "—";

    [ObservableProperty]
    private string _notesCreatedDisplay = "—";

    [ObservableProperty]
    private string _practiceTodayDisplay = "—";

    [ObservableProperty]
    private string _notesEditorTodayDisplay = "—";

    [ObservableProperty]
    private string _flashcardsModuleTodayDisplay = "—";

    public UsageSummaryWidgetViewModel(IStatisticsManager statistics, ILoggerService logger)
    {
        _statistics = statistics;
        _logger = logger;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        try
        {
            var dayKey = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var appDaily = (await _statistics.GetAsync(
                StatisticsNamespaces.App,
                AppStatKinds.DailySummary,
                dayKey).ConfigureAwait(false)).Value;
            var appTotals = (await _statistics.GetAsync(
                StatisticsNamespaces.App,
                AppStatKinds.LifetimeTotals,
                "all").ConfigureAwait(false)).Value;
            var notesTotals = (await _statistics.GetAsync(
                StatisticsNamespaces.Notes,
                NoteStatKinds.LifetimeTotals,
                "all").ConfigureAwait(false)).Value;

            LaunchCountDisplay = FormatCount(ReadInt(appTotals, "app_launch_count"));
            NotesCreatedDisplay = FormatCount(ReadInt(notesTotals, "total_notes_created"));
            PracticeTodayDisplay = FormatDuration(ReadInt(appDaily, "practice_seconds"));
            NotesEditorTodayDisplay = FormatDuration(ReadInt(appDaily, "notes_editor_seconds"));
            FlashcardsModuleTodayDisplay = FormatDuration(ReadInt(appDaily, "flashcards_module_seconds"));
        }
        catch (Exception ex)
        {
            _logger.Error("Overview", "Usage summary widget failed to load.", ex);
            LaunchCountDisplay = NotesCreatedDisplay =
                PracticeTodayDisplay = NotesEditorTodayDisplay = FlashcardsModuleTodayDisplay = "—";
        }
    }

    private static string FormatCount(long v)
        => v.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatDuration(long seconds)
    {
        if (seconds <= 0)
            return "0";

        if (seconds < 60)
            return string.Format(CultureInfo.CurrentCulture, "{0}s", seconds);

        var minutes = seconds / 60;
        if (seconds < 3600)
            return string.Format(CultureInfo.CurrentCulture, "{0} min", minutes);

        var hours = seconds / 3600;
        var remMin = (seconds % 3600) / 60;
        return remMin > 0
            ? string.Format(CultureInfo.CurrentCulture, "{0}h {1}m", hours, remMin)
            : string.Format(CultureInfo.CurrentCulture, "{0}h", hours);
    }

    private static long ReadInt(StatisticsRecord? record, string field)
    {
        if (record == null) return 0L;
        return record.Fields.TryGetValue(field, out var v) && v.Type == StatValueType.Integer
            ? v.AsInt()
            : 0L;
    }
}
