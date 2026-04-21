using System.Collections.Generic;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Flashcards.ViewModels;

/// <summary>
/// Active practice session: queue navigation, reveal, grading, and completion.
/// </summary>
public partial class FlashcardPracticeViewModel : ViewModelBase, INavigationAware
{
    private static readonly Regex ClozePattern = new(@"\{\{c\d::(.*?)}}", RegexOptions.Compiled);

    private readonly IFlashcardDeckService _deckService;
    private readonly INavigationService _navigation;

    private readonly List<Flashcard> _queue = new();
    private FlashcardSessionConfig _sessionConfig = null!;
    private string _deckId = string.Empty;

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

    public string CurrentFrontMarkdown => FormatFront(CurrentCard);

    public string CurrentBackMarkdown => FormatBack(CurrentCard);

    public bool IsQuickSession => _sessionConfig?.SessionType == FlashcardSessionType.Quick;

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

    public FlashcardPracticeViewModel(IFlashcardDeckService deckService, INavigationService navigation)
    {
        _deckService = deckService;
        _navigation = navigation;

        FlipCommand = new RelayCommand(Flip, CanFlip);
        RevealTestCommand = new RelayCommand(RevealTest, CanRevealTest);
        ExitToDeckCommand = new RelayCommand(() =>
            _navigation.NavigateTo("flashcard-deck", new FlashcardDeckNavigationParameter(_deckId)));
        ExitToLibraryCommand = new RelayCommand(() => _navigation.NavigateTo("flashcards"));
        RestartSessionCommand = new RelayCommand(Restart);
        GradeAgainCommand = new RelayCommand(() => AdvanceAfterGrade(false));
        GradeHardCommand = new RelayCommand(() => AdvanceAfterGrade(false));
        GradeGoodCommand = new RelayCommand(() => AdvanceAfterGrade(true));
        GradeEasyCommand = new RelayCommand(() => AdvanceAfterGrade(true));
        GradeLearningCommand = new RelayCommand(() => AdvanceAfterGrade(false));
        GradeGotItCommand = new RelayCommand(() => AdvanceAfterGrade(true));
        TestIncorrectCommand = new RelayCommand(() => AdvanceAfterGrade(false));
        TestCorrectCommand = new RelayCommand(() => AdvanceAfterGrade(true));
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
            NotifyCardUi();
            RefreshCommandStates();
            OnPropertyChanged(nameof(IsTestSession));
            OnPropertyChanged(nameof(IsQuickSession));
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
        NotifyCardUi();
        RefreshCommandStates();
    }

    private void Restart()
    {
        _navigation.NavigateTo(
            "flashcard-practice",
            new FlashcardPracticeNavigationParameter(_deckId, _sessionConfig));
    }

    private void AdvanceAfterGrade(bool correct)
    {
        if (CurrentCard == null || !IsFlipped || IsSessionComplete)
            return;

        if (correct)
            CorrectCount++;
        else
            IncorrectCount++;

        if (CurrentIndex >= _queue.Count - 1)
        {
            IsSessionComplete = true;
            _ = _deckService.RecordSessionOutcomeAsync(
                _sessionConfig.DeckId,
                _sessionConfig,
                CorrectCount,
                IncorrectCount,
                default);
            NotifyCardUi();
            RefreshCommandStates();
            return;
        }

        CurrentIndex++;
        IsFlipped = false;
        TestAnswer = string.Empty;
        NotifyCardUi();
        RefreshCommandStates();
    }

    private void NotifyCardUi()
    {
        OnPropertyChanged(nameof(CurrentCard));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CurrentFrontMarkdown));
        OnPropertyChanged(nameof(CurrentBackMarkdown));
        OnPropertyChanged(nameof(ShowEmptyQueue));
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(ShowActivePractice));
        OnPropertyChanged(nameof(ShowFlipButton));
        OnPropertyChanged(nameof(ShowQuickGrades));
        OnPropertyChanged(nameof(ShowStandardGrades));
        OnPropertyChanged(nameof(ShowTestGrades));
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

    private static string FormatFront(Flashcard? card)
    {
        if (card == null)
            return string.Empty;
        if (card.Type == FlashcardType.Cloze)
            return ClozePattern.Replace(card.Front, "[…]");
        return card.Front;
    }

    private static string FormatBack(Flashcard? card)
    {
        if (card == null)
            return string.Empty;
        if (card.Type == FlashcardType.Cloze)
            return ClozePattern.Replace(card.Front, "**$1**");
        return card.Back;
    }
}
