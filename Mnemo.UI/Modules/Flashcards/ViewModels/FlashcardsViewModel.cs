using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Flashcards.ViewModels;

/// <summary>
/// Library view: folders, filters, search, and deck list.
/// </summary>
public partial class FlashcardsViewModel : ViewModelBase, INavigationAware
{
    /// <summary>Filter token for due-only decks (used by the library sidebar).</summary>
    public const string DueFilterToken = "__due__";
    private const string FlashcardsSidebarOpenKey = "Flashcards.SidebarOpen";

    private readonly IFlashcardDeckService _deckService;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settingsService;
    private IReadOnlyList<FlashcardDeck> _loadedDecks = Array.Empty<FlashcardDeck>();
    private bool _sidebarStateLoaded;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Selected folder id, <see cref="DueFilterKey"/> for due-only, or <c>null</c> for all decks.
    /// </summary>
    [ObservableProperty]
    private string? _selectedFolderId;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    public ObservableCollection<FlashcardFolder> Folders { get; } = new();

    public ObservableCollection<FlashcardDeckRowViewModel> FilteredDecks { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<FlashcardDeckRowViewModel?> OpenDeckCommand { get; }

    public IRelayCommand<FlashcardDeckRowViewModel?> StartQuickSessionCommand { get; }

    public IAsyncRelayCommand CreateDeckCommand { get; }

    public IRelayCommand SelectAllDecksCommand { get; }

    public IRelayCommand SelectDueDecksCommand { get; }

    public IRelayCommand<string?> SelectFolderCommand { get; }

    public IRelayCommand ToggleSidebarCommand { get; }

    public FlashcardsViewModel(
        IFlashcardDeckService deckService,
        INavigationService navigation,
        ILocalizationService localization,
        ISettingsService settingsService)
    {
        _deckService = deckService;
        _navigation = navigation;
        _localization = localization;
        _settingsService = settingsService;

        RefreshCommand = new AsyncRelayCommand(LoadDecksAsync);
        OpenDeckCommand = new RelayCommand<FlashcardDeckRowViewModel?>(OpenDeck);
        StartQuickSessionCommand = new RelayCommand<FlashcardDeckRowViewModel?>(StartQuickSession);
        CreateDeckCommand = new AsyncRelayCommand(CreateDeckAsync);
        SelectAllDecksCommand = new RelayCommand(SelectAllDecks);
        SelectDueDecksCommand = new RelayCommand(SelectDueFilter);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        SelectFolderCommand = new RelayCommand<string?>(id =>
        {
            if (!string.IsNullOrEmpty(id))
                SelectFolder(id);
        });

        _ = LoadDecksAsync();
    }

    public void OnNavigatedTo(object? parameter)
    {
        _ = LoadDecksAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFolderIdChanged(string? value) => ApplyFilter();

    partial void OnIsSidebarOpenChanged(bool value)
    {
        if (_sidebarStateLoaded)
            _ = _settingsService.SetAsync(FlashcardsSidebarOpenKey, value);
    }

    private async Task LoadDecksAsync()
    {
        var folders = await _deckService.ListFoldersAsync().ConfigureAwait(false);
        var decks = await _deckService.ListDecksAsync().ConfigureAwait(false);
        var sidebarOpen = await _settingsService.GetAsync(FlashcardsSidebarOpenKey, true).ConfigureAwait(false);

        _loadedDecks = decks;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            _sidebarStateLoaded = false;
            IsSidebarOpen = sidebarOpen;
            _sidebarStateLoaded = true;

            Folders.Clear();
            foreach (var f in folders)
                Folders.Add(f);

            ApplyFilter();
        });
    }

    private void ApplyFilter()
    {
        var now = DateTimeOffset.UtcNow;
        IEnumerable<FlashcardDeck> query = _loadedDecks;

        if (SelectedFolderId == DueFilterToken)
            query = query.Where(d => d.Cards.Any(c => c.DueDate < now));
        else if (!string.IsNullOrEmpty(SelectedFolderId))
            query = query.Where(d => string.Equals(d.FolderId, SelectedFolderId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = query.Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        FilteredDecks.Clear();
        foreach (var d in query.OrderBy(d => d.Name))
        {
            var due = d.Cards.Count(c => c.DueDate < now);
            var dueBadge = due > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _localization.T("DueCountFormat", "Flashcards"), due)
                : string.Empty;
            var lastLine = d.LastStudied.HasValue
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _localization.T("DeckLastStudiedFormat", "Flashcards"),
                    d.LastStudied.Value.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture))
                : _localization.T("DeckNeverStudied", "Flashcards");
            var cardCountLine = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                _localization.T("DeckCardCountFormat", "Flashcards"), d.Cards.Count);

            FilteredDecks.Add(new FlashcardDeckRowViewModel
            {
                Id = d.Id,
                Name = d.Name,
                DueCount = due,
                TotalCards = d.Cards.Count,
                RetentionScore = d.RetentionScore,
                FolderId = d.FolderId,
                DueBadgeText = dueBadge,
                CardCountLine = cardCountLine,
                LastStudiedLine = lastLine
            });
        }
    }

    private void OpenDeck(FlashcardDeckRowViewModel? row)
    {
        if (row == null || string.IsNullOrEmpty(row.Id))
            return;

        _navigation.NavigateTo("flashcard-deck", new FlashcardDeckNavigationParameter(row.Id));
    }

    private void StartQuickSession(FlashcardDeckRowViewModel? row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.Id))
            return;

        var config = new FlashcardSessionConfig(
            FlashcardSessionType.Quick,
            row.Id,
            null,
            null,
            false,
            null);
        _navigation.NavigateTo("flashcard-practice", new FlashcardPracticeNavigationParameter(row.Id, config));
    }

    private async Task CreateDeckAsync()
    {
        var id = Guid.NewGuid().ToString("n");
        var name = _localization.T("DefaultDeckName", "Flashcards");
        var deck = new FlashcardDeck(
            id,
            name,
            null,
            Array.Empty<string>(),
            null,
            0,
            Array.Empty<Flashcard>());

        await _deckService.SaveDeckAsync(deck).ConfigureAwait(false);
        await LoadDecksAsync().ConfigureAwait(false);
        _navigation.NavigateTo("flashcard-deck", new FlashcardDeckNavigationParameter(id));
    }

    public void SelectAllDecks() => SelectedFolderId = null;

    public void SelectDueFilter() => SelectedFolderId = DueFilterToken;

    public void SelectFolder(string folderId) => SelectedFolderId = folderId;

    public void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;
}
