using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Notes.Markdown;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Flashcards.ViewModels;

/// <summary>
/// Minimal view-model for a single embedded image rendered in the practice card
/// (path + alignment hint taken from the <c>![alt](path){align=...}</c> markdown token).
/// </summary>
public sealed record FlashcardPracticeImage(string Path, string Align);

/// <summary>
/// Active practice session: queue navigation, reveal, grading, and completion.
/// </summary>
public partial class FlashcardPracticeViewModel : ViewModelBase, INavigationAware
{
    private static readonly Regex ClozePattern = new(@"\{\{c\d+::(.*?)}}", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Matches the image tokens injected by <c>RichDocumentEditor</c> into Front/Back text:
    /// <c>![alt](path){align=left|center|right}</c>. The inner <c>path</c> group captures the raw
    /// local file path (may be Windows-style), and the optional <c>align</c> group drives layout.
    /// </summary>
    private static readonly Regex ImageTokenPattern = new(
        @"!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)(?:\{align=(?<align>left|center|right)\})?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IFlashcardDeckService _deckService;
    private readonly INavigationService _navigation;
    private readonly IOverlayService _overlay;
    private readonly ILocalizationService _localization;

    private readonly List<Flashcard> _queue = new();
    private readonly List<FlashcardSessionCardResult> _sessionResults = new();
    private FlashcardSessionConfig _sessionConfig = null!;
    private string _deckId = string.Empty;
    private DateTimeOffset _sessionStartedAt;

    [ObservableProperty]
    private string _deckName = string.Empty;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private bool _isFlipped;

    [ObservableProperty]
    private bool _isSessionComplete;

    [ObservableProperty]
    private int _correctCount;

    [ObservableProperty]
    private int _incorrectCount;

    [ObservableProperty]
    private string _testAnswer = string.Empty;

    [ObservableProperty]
    private string _nextAgainInterval = string.Empty;

    [ObservableProperty]
    private string _nextHardInterval = string.Empty;

    [ObservableProperty]
    private string _nextGoodInterval = string.Empty;

    [ObservableProperty]
    private string _nextEasyInterval = string.Empty;

    [ObservableProperty]
    private string _summaryDuration = "0m";

    [ObservableProperty]
    private string _summaryAccuracy = "0%";

    [ObservableProperty]
    private string _summaryNextDue = "-";

    public Flashcard? CurrentCard =>
        _queue.Count > 0 && CurrentIndex >= 0 && CurrentIndex < _queue.Count
            ? _queue[CurrentIndex]
            : null;

    public double ProgressFraction =>
        _queue.Count == 0 ? 0 : (double)CurrentIndex / _queue.Count;

    public double ProgressPercent
    {
        get
        {
            if (_queue.Count == 0)
                return 0;
            if (IsSessionComplete)
                return 100;
            return Math.Clamp(100.0 * CurrentIndex / _queue.Count, 0, 100);
        }
    }

    public bool ShowEmptyQueue => IsSessionComplete && _queue.Count == 0;

    public bool ShowSummary => IsSessionComplete && _queue.Count > 0;

    public bool ShowActivePractice => !IsSessionComplete && _queue.Count > 0;

    public bool ShowFlipButton => !IsTestSession && !IsFlipped && !IsSessionComplete && _queue.Count > 0;

    public bool ShowQuickGrades => IsQuickSession && IsFlipped && !IsSessionComplete;

    public bool ShowStandardGrades => ShowFullGrades && IsFlipped && !IsSessionComplete;

    public bool ShowTestGrades => IsTestSession && IsFlipped && !IsSessionComplete;

    public string ProgressText =>
        _queue.Count == 0 ? "0 / 0" : $"{CurrentIndex + 1} / {_queue.Count}";

    /// <summary>Front markdown with images stripped and cloze answers hidden (for <c>MarkdownView</c>).</summary>
    public string CurrentFrontMarkdown => BuildFrontMarkdown(CurrentCard);

    /// <summary>Back markdown with images stripped and cloze answers revealed (for <c>MarkdownView</c>).</summary>
    public string CurrentBackMarkdown => BuildBackMarkdown(CurrentCard);

    /// <summary>Front spans for practice rendering with formatting/LaTeX preserved.</summary>
    public IReadOnlyList<InlineSpan> CurrentFrontSpans => BuildFrontSpans(CurrentCard);

    /// <summary>Back spans for practice rendering with formatting/LaTeX preserved.</summary>
    public IReadOnlyList<InlineSpan> CurrentBackSpans => BuildBackSpans(CurrentCard);

    public IReadOnlyList<FlashcardPracticeImage> CurrentFrontImages => ExtractImages(CurrentCard?.Front);

    public IReadOnlyList<FlashcardPracticeImage> CurrentBackImages => ExtractImages(
        IsClozeCard(CurrentCard) ? CurrentCard?.Front : CurrentCard?.Back);

    public bool HasFrontImages => CurrentFrontImages.Count > 0;

    public bool HasBackImages => CurrentBackImages.Count > 0;

    public int CardsReviewed => CorrectCount + IncorrectCount;

    public bool IsQuickSession => _sessionConfig?.SessionType == FlashcardSessionType.Quick;

    public bool IsFocusedSession => _sessionConfig?.SessionType == FlashcardSessionType.Focused;

    public bool IsCramSession => _sessionConfig?.SessionType == FlashcardSessionType.Cram;

    public bool IsTestSession => _sessionConfig?.SessionType == FlashcardSessionType.Test;

    public bool ShowFullGrades =>
        _sessionConfig?.SessionType is FlashcardSessionType.Focused or FlashcardSessionType.Cram;

    public IRelayCommand FlipCommand { get; }

    public IRelayCommand RevealTestCommand { get; }

    public IRelayCommand ExitToDeckCommand { get; }

    public IRelayCommand ExitToLibraryCommand { get; }

    public IRelayCommand RestartSessionCommand { get; }

    public IRelayCommand GradeAgainCommand { get; }

    public IRelayCommand GradeHardCommand { get; }

    public IRelayCommand GradeGoodCommand { get; }

    public IRelayCommand GradeEasyCommand { get; }

    public IRelayCommand GradeLearningCommand { get; }

    public IRelayCommand GradeGotItCommand { get; }

    public IRelayCommand TestIncorrectCommand { get; }

    public IRelayCommand TestCorrectCommand { get; }

    public IRelayCommand QuickGradeLearningHotkeyCommand { get; }

    public IRelayCommand QuickGradeGotItHotkeyCommand { get; }

    public FlashcardPracticeViewModel(
        IFlashcardDeckService deckService,
        INavigationService navigation,
        IOverlayService overlay,
        ILocalizationService localization)
    {
        _deckService = deckService;
        _navigation = navigation;
        _overlay = overlay;
        _localization = localization;

        FlipCommand = new RelayCommand(Flip, CanFlip);
        RevealTestCommand = new RelayCommand(RevealTest, CanRevealTest);
        ExitToDeckCommand = new RelayCommand(() => _ = ExitToDeckAsync());
        ExitToLibraryCommand = new RelayCommand(() => _navigation.NavigateTo("flashcards"));
        RestartSessionCommand = new RelayCommand(Restart);
        GradeAgainCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Again));
        GradeHardCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Hard));
        GradeGoodCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Good));
        GradeEasyCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Easy));
        GradeLearningCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Again));
        GradeGotItCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Good));
        TestIncorrectCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Again));
        TestCorrectCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Good));
        QuickGradeLearningHotkeyCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Again));
        QuickGradeGotItHotkeyCommand = new RelayCommand(() => AdvanceAfterGrade(FlashcardReviewGrade.Good));
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not FlashcardPracticeNavigationParameter p)
        {
            _navigation.NavigateTo("flashcards");
            return;
        }

        _deckId = p.DeckId;
        _sessionConfig = p.SessionConfig;
        OnPropertyChanged(nameof(IsQuickSession));
        OnPropertyChanged(nameof(IsFocusedSession));
        OnPropertyChanged(nameof(IsCramSession));
        OnPropertyChanged(nameof(IsTestSession));
        OnPropertyChanged(nameof(ShowFullGrades));
        _ = InitializeAsync(p);
    }

    private async Task InitializeAsync(FlashcardPracticeNavigationParameter parameter)
    {
        var deck = await _deckService.GetDeckByIdAsync(parameter.DeckId, default).ConfigureAwait(false);
        if (deck == null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _navigation.NavigateTo("flashcards"));
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var random = parameter.SessionConfig.SessionType == FlashcardSessionType.Cram && parameter.SessionConfig.Shuffle
            ? new Random()
            : null;
        var built = FlashcardSessionQueueBuilder.BuildQueue(deck, parameter.SessionConfig, now, random);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            DeckName = deck.Name;
            _queue.Clear();
            _queue.AddRange(built);
            CurrentIndex = 0;
            IsFlipped = false;
            IsSessionComplete = _queue.Count == 0;
            TestAnswer = string.Empty;
            CorrectCount = 0;
            IncorrectCount = 0;
            _sessionStartedAt = DateTimeOffset.UtcNow;
            _sessionResults.Clear();
            NotifyCardUi();
            RefreshCommandStates();
            OnPropertyChanged(nameof(IsTestSession));
            OnPropertyChanged(nameof(IsQuickSession));
            OnPropertyChanged(nameof(IsFocusedSession));
            OnPropertyChanged(nameof(IsCramSession));
            OnPropertyChanged(nameof(ShowFullGrades));
            OnPropertyChanged(nameof(ShowFlipButton));
        });
    }

    private bool CanFlip() =>
        !IsSessionComplete
        && !IsTestSession
        && CurrentCard != null
        && !IsFlipped;

    private void Flip()
    {
        if (!CanFlip())
            return;
        IsFlipped = true;
        UpdateIntervalHints();
        NotifyCardUi();
        RefreshCommandStates();
    }

    private bool CanRevealTest() =>
        !IsSessionComplete
        && IsTestSession
        && CurrentCard != null
        && !IsFlipped
        && !string.IsNullOrWhiteSpace(TestAnswer);

    private void RevealTest()
    {
        if (!CanRevealTest())
            return;
        IsFlipped = true;
        UpdateIntervalHints();
        NotifyCardUi();
        RefreshCommandStates();
    }

    private async Task ExitToDeckAsync()
    {
        if (!IsSessionComplete && CardsReviewed > 0)
        {
            var leaveLabel = _localization.T("ExitPractice", "Flashcards");
            var cancelLabel = _localization.T("Cancel", "Common");
            var result = await _overlay.CreateDialogAsync(
                _localization.T("ExitSessionTitle", "Flashcards"),
                _localization.T("ExitSessionConfirm", "Flashcards"),
                leaveLabel,
                cancelLabel).ConfigureAwait(false);
            if (!string.Equals(result, leaveLabel, StringComparison.Ordinal))
                return;
        }

        _navigation.NavigateTo("flashcard-deck", new FlashcardDeckNavigationParameter(_deckId));
    }

    private void Restart()
    {
        _navigation.NavigateTo(
            "flashcard-practice",
            new FlashcardPracticeNavigationParameter(_deckId, _sessionConfig));
    }

    private void AdvanceAfterGrade(FlashcardReviewGrade grade)
    {
        if (CurrentCard == null || !IsFlipped || IsSessionComplete)
            return;

        if (grade is FlashcardReviewGrade.Good or FlashcardReviewGrade.Easy)
            CorrectCount++;
        else
            IncorrectCount++;
        _sessionResults.Add(new FlashcardSessionCardResult(CurrentCard.Id, grade, DateTimeOffset.UtcNow));

        if (IsFocusedSession && _sessionConfig.TimeLimitMinutes is > 0)
        {
            var elapsed = DateTimeOffset.UtcNow - _sessionStartedAt;
            if (elapsed.TotalMinutes >= _sessionConfig.TimeLimitMinutes.Value)
            {
                CompleteSessionAsync();
                return;
            }
        }

        if (CurrentIndex >= _queue.Count - 1)
        {
            CompleteSessionAsync();
            return;
        }

        CurrentIndex++;
        IsFlipped = false;
        TestAnswer = string.Empty;
        NotifyCardUi();
        RefreshCommandStates();
    }

    private void CompleteSessionAsync()
    {
        if (IsSessionComplete)
            return;

        IsSessionComplete = true;
        var completedAt = DateTimeOffset.UtcNow;
        var result = new FlashcardSessionResult(
            _sessionConfig.DeckId,
            _sessionConfig,
            _sessionStartedAt,
            completedAt,
            _sessionResults.ToArray());
        _ = _deckService.RecordSessionOutcomeAsync(result, default);
        BuildSummary(completedAt);
        NotifyCardUi();
        RefreshCommandStates();
    }

    private void BuildSummary(DateTimeOffset completedAt)
    {
        var reviewed = Math.Max(1, CardsReviewed);
        var accuracy = (double)CorrectCount / reviewed * 100d;
        SummaryAccuracy = $"{Math.Round(accuracy, MidpointRounding.AwayFromZero):0}%";

        var duration = completedAt - _sessionStartedAt;
        SummaryDuration = duration.TotalMinutes >= 1
            ? $"{Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero):0}m"
            : $"{Math.Max(1, Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero)):0}s";

        var now = DateTimeOffset.UtcNow;
        var earliest = _sessionResults
            .Select(r => FlashcardScheduling.ApplyGrade(CurrentCard ?? new Flashcard(
                r.CardId, _deckId, string.Empty, string.Empty, FlashcardType.Classic, Array.Empty<string>(), now, 1d, 5d, 0.8d), r.Grade, now).DueDate)
            .DefaultIfEmpty(now)
            .Min();
        var delta = earliest - now;
        SummaryNextDue = delta.TotalDays >= 1
            ? $"{Math.Round(delta.TotalDays, MidpointRounding.AwayFromZero):0}d"
            : $"{Math.Max(1, Math.Round(delta.TotalHours, MidpointRounding.AwayFromZero)):0}h";
    }

    private void UpdateIntervalHints()
    {
        if (CurrentCard is null)
            return;
        var now = DateTimeOffset.UtcNow;
        NextAgainInterval = FlashcardScheduling.DescribeInterval(CurrentCard, FlashcardReviewGrade.Again, now);
        NextHardInterval = FlashcardScheduling.DescribeInterval(CurrentCard, FlashcardReviewGrade.Hard, now);
        NextGoodInterval = FlashcardScheduling.DescribeInterval(CurrentCard, FlashcardReviewGrade.Good, now);
        NextEasyInterval = FlashcardScheduling.DescribeInterval(CurrentCard, FlashcardReviewGrade.Easy, now);
    }

    private void NotifyCardUi()
    {
        OnPropertyChanged(nameof(CurrentCard));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CurrentFrontMarkdown));
        OnPropertyChanged(nameof(CurrentBackMarkdown));
        OnPropertyChanged(nameof(CurrentFrontSpans));
        OnPropertyChanged(nameof(CurrentBackSpans));
        OnPropertyChanged(nameof(CurrentFrontImages));
        OnPropertyChanged(nameof(CurrentBackImages));
        OnPropertyChanged(nameof(HasFrontImages));
        OnPropertyChanged(nameof(HasBackImages));
        OnPropertyChanged(nameof(ShowEmptyQueue));
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(ShowActivePractice));
        OnPropertyChanged(nameof(ShowFlipButton));
        OnPropertyChanged(nameof(ShowQuickGrades));
        OnPropertyChanged(nameof(ShowStandardGrades));
        OnPropertyChanged(nameof(ShowTestGrades));
        OnPropertyChanged(nameof(CardsReviewed));
    }

    private void RefreshCommandStates()
    {
        FlipCommand.NotifyCanExecuteChanged();
        RevealTestCommand.NotifyCanExecuteChanged();
    }

    partial void OnTestAnswerChanged(string value) => RevealTestCommand.NotifyCanExecuteChanged();

    partial void OnIsFlippedChanged(bool value)
    {
        NotifyCardUi();
        RefreshCommandStates();
    }

    partial void OnIsSessionCompleteChanged(bool value)
    {
        NotifyCardUi();
        RefreshCommandStates();
    }

    partial void OnCurrentIndexChanged(int value) => NotifyCardUi();

    private static string BuildFrontMarkdown(Flashcard? card)
    {
        if (card == null)
            return string.Empty;

        var raw = StripImages(card.Front ?? string.Empty);
        return IsClozeCard(card)
            ? ClozePattern.Replace(raw, "[…]")
            : raw;
    }

    private static string BuildBackMarkdown(Flashcard? card)
    {
        if (card == null)
            return string.Empty;

        if (IsClozeCard(card))
        {
            var raw = StripImages(card.Front ?? string.Empty);
            return ClozePattern.Replace(raw, "$1");
        }

        return StripImages(card.Back ?? string.Empty);
    }

    private static IReadOnlyList<InlineSpan> BuildFrontSpans(Flashcard? card)
    {
        if (card == null)
            return new List<InlineSpan> { InlineSpan.Plain(string.Empty) };

        var spans = BuildEditorSpans(card.FrontBlocks, card.Front ?? string.Empty);
        spans = StripImagesFromSpans(spans);
        return IsClozeCard(card)
            ? ReplaceClozeInSpans(spans, "[…]")
            : spans;
    }

    private static IReadOnlyList<InlineSpan> BuildBackSpans(Flashcard? card)
    {
        if (card == null)
            return new List<InlineSpan> { InlineSpan.Plain(string.Empty) };

        if (IsClozeCard(card))
        {
            var frontSpans = BuildEditorSpans(card.FrontBlocks, card.Front ?? string.Empty);
            frontSpans = StripImagesFromSpans(frontSpans);
            return ReplaceClozeInSpans(frontSpans, "$1");
        }

        var backSpans = BuildEditorSpans(card.BackBlocks, card.Back ?? string.Empty);
        return StripImagesFromSpans(backSpans);
    }

    private static bool IsClozeCard(Flashcard? card) =>
        card != null
        && card.Type == FlashcardType.Cloze
        && ClozePattern.IsMatch(card.Front ?? string.Empty);

    private static IReadOnlyList<InlineSpan> BuildEditorSpans(IReadOnlyList<Block>? blocks, string fallbackText)
    {
        if (blocks is null || blocks.Count == 0)
            return InlineMarkdownParser.ToSpans(fallbackText);

        // Legacy cards can have a single plain text block whose span text still contains markdown
        // (including `$...$` LaTeX). Re-parse that shape so practice mode renders equations.
        if (LooksLikeLegacyPlainTextBlocks(blocks))
            return InlineMarkdownParser.ToSpans(fallbackText);

        var merged = new List<InlineSpan>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            block.EnsureSpans();
            if (block.Spans is { Count: > 0 })
                merged.AddRange(block.Spans);
            if (i < blocks.Count - 1)
                merged.Add(InlineSpan.Plain("\n"));
        }

        return InlineSpanFormatApplier.Normalize(merged);
    }

    private static bool LooksLikeLegacyPlainTextBlocks(IReadOnlyList<Block> blocks)
    {
        if (blocks.Count != 1)
            return false;

        var block = blocks[0];
        if (block.Type != BlockType.Text)
            return false;

        if (block.Spans is not { Count: 1 })
            return false;

        return block.Spans[0] is TextSpan { Style: var style } textSpan
               && style.Equals(TextStyle.Default)
               && !string.IsNullOrEmpty(textSpan.Text);
    }

    private static IReadOnlyList<InlineSpan> StripImagesFromSpans(IReadOnlyList<InlineSpan> spans)
    {
        var oldFlat = InlineSpanFormatApplier.Flatten(spans);
        var newFlat = StripImages(oldFlat);
        if (string.Equals(oldFlat, newFlat, StringComparison.Ordinal))
            return spans;
        return InlineSpanFormatApplier.Normalize(
            InlineSpanFormatApplier.ApplyTextEdit(spans, oldFlat, newFlat));
    }

    private static IReadOnlyList<InlineSpan> ReplaceClozeInSpans(IReadOnlyList<InlineSpan> spans, string replacementPattern)
    {
        var oldFlat = InlineSpanFormatApplier.Flatten(spans);
        var newFlat = ClozePattern.Replace(oldFlat, replacementPattern);
        if (string.Equals(oldFlat, newFlat, StringComparison.Ordinal))
            return spans;
        return InlineSpanFormatApplier.Normalize(
            InlineSpanFormatApplier.ApplyTextEdit(spans, oldFlat, newFlat));
    }

    private static string StripImages(string text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : ImageTokenPattern.Replace(text, string.Empty).Trim();

    private static IReadOnlyList<FlashcardPracticeImage> ExtractImages(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<FlashcardPracticeImage>();

        var matches = ImageTokenPattern.Matches(text);
        if (matches.Count == 0)
            return Array.Empty<FlashcardPracticeImage>();

        var list = new List<FlashcardPracticeImage>(matches.Count);
        foreach (Match m in matches)
        {
            var path = m.Groups["path"].Value.Trim();
            if (string.IsNullOrEmpty(path))
                continue;
            var align = m.Groups["align"].Success ? m.Groups["align"].Value : "center";
            list.Add(new FlashcardPracticeImage(path, align));
        }
        return list;
    }
}
