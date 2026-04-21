using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Flashcards.ViewModels;

/// <summary>
/// Deck editor: cards, study launcher, and persistence through <see cref="IFlashcardDeckService"/>.
/// </summary>
public partial class FlashcardDeckDetailViewModel : ViewModelBase, INavigationAware
{
    private static readonly Regex ClozeOrdinalPattern = new(@"\{\{c(\d+)::", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClozeContentPattern = new(@"\{\{c\d+::(.*?)}}", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private readonly IFlashcardDeckService _deckService;
    private readonly INavigationService _navigation;
    private readonly IOverlayService _overlay;
    private readonly ILocalizationService _localization;

    private string _deckId = string.Empty;

    [ObservableProperty]
    private string _deckName = string.Empty;

    [ObservableProperty]
    private Flashcard? _selectedCard;

    [ObservableProperty]
    private string _editorFront = string.Empty;

    [ObservableProperty]
    private string _editorBack = string.Empty;

    [ObservableProperty]
    private bool _isSessionLauncherOpen;

    [ObservableProperty]
    private double _focusedCardCountSlider = 20;

    [ObservableProperty]
    private bool _cramShuffle;

    [ObservableProperty]
    private int _totalCardsCount;

    [ObservableProperty]
    private int _dueTodayCount;

    [ObservableProperty]
    private int _retentionPercent;

    [ObservableProperty]
    private string? _expandedCardId;

    [ObservableProperty]
    private string _editorTags = string.Empty;

    [ObservableProperty]
    private bool _isCardEditorWriteMode = true;

    public ObservableCollection<Flashcard> Cards { get; } = new();

    public string LocalizedCardDue => _localization.T("CardDue", "Flashcards");

    /// <summary>Front source for markdown preview in the card editor (cloze → hidden blanks).</summary>
    public string EditorFrontPreviewMarkdown =>
        FrontIndicatesCloze(EditorFront)
            ? ClozeContentPattern.Replace(EditorFront, "[…]")
            : EditorFront;

    /// <summary>Back source for preview; cloze cards use the front with blanks emphasized like practice mode.</summary>
    public string EditorBackPreviewMarkdown =>
        FrontIndicatesCloze(EditorFront)
            ? ClozeContentPattern.Replace(EditorFront, "**$1**")
            : EditorBack;

    public IRelayCommand GoBackCommand { get; }

    public IRelayCommand OpenStudyLauncherCommand { get; }

    public IRelayCommand CloseStudyLauncherCommand { get; }

    public IRelayCommand StartQuickSessionCommand { get; }

    public IRelayCommand StartFocusedSessionCommand { get; }

    public IRelayCommand StartCramSessionCommand { get; }

    public IRelayCommand StartTestSessionCommand { get; }

    public IAsyncRelayCommand SaveDeckCommand { get; }

    public IRelayCommand AddCardCommand { get; }

    public IAsyncRelayCommand SaveCardCommand { get; }

    public IRelayCommand CancelEditCommand { get; }

    public IAsyncRelayCommand DeleteCardCommand { get; }

    public IAsyncRelayCommand DeleteDeckCommand { get; }

    public IRelayCommand<Flashcard?> ToggleCardRowCommand { get; }

    public IRelayCommand CloseExpandedCardCommand { get; }

    public IRelayCommand SelectCardEditorWriteModeCommand { get; }

    public IRelayCommand SelectCardEditorPreviewModeCommand { get; }

    public FlashcardDeckDetailViewModel(
        IFlashcardDeckService deckService,
        INavigationService navigation,
        IOverlayService overlay,
        ILocalizationService localization)
    {
        _deckService = deckService;
        _navigation = navigation;
        _overlay = overlay;
        _localization = localization;

        GoBackCommand = new RelayCommand(() => _navigation.NavigateTo("flashcards"));
        OpenStudyLauncherCommand = new RelayCommand(() => IsSessionLauncherOpen = true);
        CloseStudyLauncherCommand = new RelayCommand(() => IsSessionLauncherOpen = false);
        StartQuickSessionCommand = new RelayCommand(() => StartPractice(new FlashcardSessionConfig(
            FlashcardSessionType.Quick, _deckId, null, null, false, null)));
        StartFocusedSessionCommand = new RelayCommand(() => StartPractice(new FlashcardSessionConfig(
            FlashcardSessionType.Focused, _deckId, (int)Math.Clamp(FocusedCardCountSlider, 1, 500), null, false, null)));
        StartCramSessionCommand = new RelayCommand(() => StartPractice(new FlashcardSessionConfig(
            FlashcardSessionType.Cram, _deckId, null, null, CramShuffle, null)));
        StartTestSessionCommand = new RelayCommand(() => StartPractice(new FlashcardSessionConfig(
            FlashcardSessionType.Test, _deckId, null, null, false, FlashcardTestGradingMode.Exact)));
        SaveDeckCommand = new AsyncRelayCommand(SaveDeckAsync);
        AddCardCommand = new RelayCommand(AddCard);
        SaveCardCommand = new AsyncRelayCommand(SaveCardAsync, () => SelectedCard != null);
        CancelEditCommand = new RelayCommand(CancelEdit);
        DeleteCardCommand = new AsyncRelayCommand(DeleteCardAsync, () => SelectedCard != null);
        DeleteDeckCommand = new AsyncRelayCommand(DeleteDeckAsync);
        ToggleCardRowCommand = new RelayCommand<Flashcard?>(ToggleCardRow);
        CloseExpandedCardCommand = new RelayCommand(() => ExpandedCardId = null);
        SelectCardEditorWriteModeCommand = new RelayCommand(() => IsCardEditorWriteMode = true);
        SelectCardEditorPreviewModeCommand = new RelayCommand(() => IsCardEditorWriteMode = false);

        Cards.CollectionChanged += OnCardsCollectionChanged;
    }

    partial void OnEditorFrontChanged(string value)
    {
        OnPropertyChanged(nameof(EditorFrontPreviewMarkdown));
        OnPropertyChanged(nameof(EditorBackPreviewMarkdown));
    }

    partial void OnEditorBackChanged(string value) =>
        OnPropertyChanged(nameof(EditorBackPreviewMarkdown));

    private void ToggleCardRow(Flashcard? card)
    {
        if (card is null)
            return;
        if (ExpandedCardId == card.Id)
        {
            ExpandedCardId = null;
            return;
        }

        if (SelectedCard?.Id != card.Id)
        {
            SelectedCard = card;
            return;
        }

        EditorFront = card.Front;
        EditorBack = card.Back;
        EditorTags = string.Join(", ", card.Tags);
        IsCardEditorWriteMode = true;
        ExpandedCardId = card.Id;
    }

    private void OnCardsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCardStats();

    public void OnNavigatedTo(object? parameter)
    {
        var id = parameter switch
        {
            FlashcardDeckNavigationParameter p => p.DeckId,
            string s => s,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(id))
        {
            _navigation.NavigateTo("flashcards");
            return;
        }

        _deckId = id;
        _ = LoadDeckAsync();
    }

    public bool HasSelectedCard => SelectedCard != null;

    partial void OnSelectedCardChanged(Flashcard? value)
    {
        OnPropertyChanged(nameof(HasSelectedCard));
        SaveCardCommand.NotifyCanExecuteChanged();
        DeleteCardCommand.NotifyCanExecuteChanged();
        if (value == null)
        {
            EditorFront = string.Empty;
            EditorBack = string.Empty;
            EditorTags = string.Empty;
            ExpandedCardId = null;
            IsCardEditorWriteMode = true;
            return;
        }

        EditorFront = value.Front;
        EditorBack = value.Back;
        EditorTags = string.Join(", ", value.Tags);
        IsCardEditorWriteMode = true;
        ExpandedCardId = value.Id;
    }

    private async Task LoadDeckAsync()
    {
        var deck = await _deckService.GetDeckByIdAsync(_deckId, default).ConfigureAwait(false);
        if (deck == null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _navigation.NavigateTo("flashcards"));
            return;
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            DeckName = deck.Name;
            RetentionPercent = deck.RetentionScore;
            Cards.Clear();
            foreach (var c in deck.Cards)
                Cards.Add(c);
            SelectedCard = null;
            ExpandedCardId = null;
            RefreshCardStats();
        });
    }

    private void RefreshCardStats()
    {
        TotalCardsCount = Cards.Count;
        DueTodayCount = Cards.Count(c => c.DueDate.LocalDateTime.Date <= DateTime.Today);
    }

    private void StartPractice(FlashcardSessionConfig config)
    {
        IsSessionLauncherOpen = false;
        _navigation.NavigateTo("flashcard-practice", new FlashcardPracticeNavigationParameter(_deckId, config));
    }

    private async Task SaveDeckAsync()
    {
        var deck = await BuildDeckModelAsync().ConfigureAwait(false);
        await _deckService.SaveDeckAsync(deck).ConfigureAwait(false);
    }

    private async Task<FlashcardDeck> BuildDeckModelAsync()
    {
        var existing = await _deckService.GetDeckByIdAsync(_deckId, default).ConfigureAwait(false);
        return new FlashcardDeck(
            _deckId,
            DeckName,
            existing?.FolderId,
            existing?.Tags ?? Array.Empty<string>(),
            existing?.LastStudied,
            existing?.RetentionScore ?? 0,
            Cards.ToArray());
    }

    private void AddCard()
    {
        var id = Guid.NewGuid().ToString("n");
        var card = new Flashcard(
            id,
            _deckId,
            string.Empty,
            string.Empty,
            FlashcardType.Classic,
            Array.Empty<string>(),
            DateTimeOffset.UtcNow,
            null,
            null,
            null);
        Cards.Add(card);
        SelectedCard = card;
    }

    private async Task SaveCardAsync()
    {
        if (SelectedCard == null)
            return;

        var idx = Cards.IndexOf(SelectedCard);
        if (idx < 0)
            return;

        var updated = SelectedCard with
        {
            Front = EditorFront,
            Back = EditorBack,
            Type = FrontIndicatesCloze(EditorFront) ? FlashcardType.Cloze : FlashcardType.Classic,
            Tags = ParseEditorTags(EditorTags)
        };
        Cards[idx] = updated;
        SelectedCard = updated;
        await SaveDeckAsync().ConfigureAwait(false);
    }

    private static string[] ParseEditorTags(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToArray();

    private void CancelEdit()
    {
        if (SelectedCard == null)
            return;
        EditorFront = SelectedCard.Front;
        EditorBack = SelectedCard.Back;
        EditorTags = string.Join(", ", SelectedCard.Tags);
        IsCardEditorWriteMode = true;
        ExpandedCardId = null;
    }

    private async Task DeleteCardAsync()
    {
        if (SelectedCard == null)
            return;

        var deleteLabel = _localization.T("Delete", "Common");
        var cancelLabel = _localization.T("Cancel", "Common");
        var confirm = await _overlay.CreateDialogAsync(
            _localization.T("DeleteCard", "Flashcards"),
            _localization.T("DeleteCardConfirm", "Flashcards"),
            deleteLabel,
            cancelLabel).ConfigureAwait(false);

        if (!string.Equals(confirm, deleteLabel, StringComparison.Ordinal))
            return;

        var removedId = SelectedCard.Id;
        Cards.Remove(SelectedCard);
        if (ExpandedCardId == removedId)
            ExpandedCardId = null;
        SelectedCard = Cards.FirstOrDefault();
        await SaveDeckAsync().ConfigureAwait(false);
    }

    private async Task DeleteDeckAsync()
    {
        var deleteLabel = _localization.T("Delete", "Common");
        var cancelLabel = _localization.T("Cancel", "Common");
        var confirm = await _overlay.CreateDialogAsync(
            _localization.T("DeleteDeck", "Flashcards"),
            _localization.T("DeleteDeckConfirm", "Flashcards"),
            deleteLabel,
            cancelLabel).ConfigureAwait(false);

        if (!string.Equals(confirm, deleteLabel, StringComparison.Ordinal))
            return;

        await _deckService.DeleteDeckAsync(_deckId, default).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _navigation.NavigateTo("flashcards"));
    }

    /// <summary>Matches practice session cloze markup: <c>{{c1::hidden}}</c>.</summary>
    public static bool FrontIndicatesCloze(string front) =>
        front.Length > 0 && ClozeOrdinalPattern.IsMatch(front);

    /// <summary>Inserts or wraps the next <c>{{cN::…}}</c> marker; <paramref name="selEnd"/> is exclusive.</summary>
    public static (string NewText, int CaretPosition) BuildFrontWithClozeInserted(string front, int selStart, int selEnd)
    {
        selStart = Math.Clamp(selStart, 0, front.Length);
        selEnd = Math.Clamp(selEnd, 0, front.Length);
        if (selEnd < selStart)
            (selStart, selEnd) = (selEnd, selStart);

        var maxOrdinal = 0;
        foreach (Match m in ClozeOrdinalPattern.Matches(front))
        {
            if (int.TryParse(m.Groups[1].Value, out var o))
                maxOrdinal = Math.Max(maxOrdinal, o);
        }

        var n = maxOrdinal + 1;
        var before = front[..selStart];
        var after = front[selEnd..];
        var selectedLength = selEnd - selStart;
        var selected = selectedLength > 0 ? front.Substring(selStart, selectedLength) : string.Empty;

        var wrapped = selected.Length > 0
            ? $"{{{{c{n}::{selected}}}}}"
            : $"{{{{c{n}::}}}}";

        var newText = before + wrapped + after;
        var caret = selected.Length > 0
            ? selStart + wrapped.Length
            : selStart + wrapped.IndexOf("::", StringComparison.Ordinal) + 2;
        return (newText, caret);
    }
}
