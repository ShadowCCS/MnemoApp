using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Search;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components.Overlays;

public sealed partial class GlobalSearchOverlayViewModel : ViewModelBase
{
    private readonly IGlobalSearchService _globalSearchService;
    private readonly INavigationService _navigationService;
    private readonly ILocalizationService _localization;
    private CancellationTokenSource? _searchCts;
    private string _lastQueryText = string.Empty;
    private readonly Dictionary<string, int> _groupLimits = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<GlobalSearchItemViewModel> BestMatches { get; } = [];
    public ObservableCollection<GlobalSearchGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _selectedItemId;

    [ObservableProperty]
    private string _placeholderText;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public Action? CloseAction { get; set; }

    public GlobalSearchOverlayViewModel(
        IGlobalSearchService globalSearchService,
        INavigationService navigationService,
        ILocalizationService localization,
        string placeholderText)
    {
        _globalSearchService = globalSearchService;
        _navigationService = navigationService;
        _localization = localization;
        _placeholderText = placeholderText;
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchAsync(value);
    }

    [RelayCommand]
    private void Close()
    {
        _searchCts?.Cancel();
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private async Task LoadMoreAsync(GlobalSearchGroupViewModel? group)
    {
        if (group == null || string.IsNullOrWhiteSpace(_lastQueryText))
        {
            return;
        }

        var current = _groupLimits.TryGetValue(group.GroupKey, out var limit)
            ? limit
            : group.Items.Count;
        _groupLimits[group.GroupKey] = Math.Max(10, current + 10);
        await SearchAsync(_lastQueryText, preserveSelection: true).ConfigureAwait(false);
    }

    [RelayCommand]
    private Task OpenItemAsync(GlobalSearchItemViewModel? item) => item == null
        ? Task.CompletedTask
        : ActivateItemAsync(item);

    public async Task SearchAsync(string text, bool preserveSelection = false)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        _lastQueryText = text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_lastQueryText))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Groups.Clear();
                BestMatches.Clear();
                SelectedItemId = null;
                StatusText = string.Empty;
                IsLoading = false;
            });
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);

        try
        {
            await Task.Delay(120, ct).ConfigureAwait(false);

            var query = SearchQuery.Create(
                _lastQueryText,
                limitPerGroup: 10,
                groupLimits: _groupLimits,
                fuzzy: true);

            var response = await _globalSearchService.SearchAsync(query, ct).ConfigureAwait(false);
            var bestMatches = response.BestMatches
                .Select(item => new GlobalSearchItemViewModel(item, _localization))
                .ToList();
            var vmGroups = response.Groups.Select(group => new GlobalSearchGroupViewModel(
                group.GroupKey,
                group.GroupDisplayName,
                group.TotalMatched,
                group.HasMore,
                group.Items.Select(item => new GlobalSearchItemViewModel(item, _localization)).ToList())).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Groups.Clear();
                BestMatches.Clear();
                foreach (var best in bestMatches)
                {
                    BestMatches.Add(best);
                }
                foreach (var group in vmGroups)
                {
                    Groups.Add(group);
                }

                StatusText = vmGroups.Sum(g => g.TotalMatched) == 0 && bestMatches.Count == 0
                    ? _localization.T("NoResults", "GlobalSearch")
                    : string.Empty;

                if (!preserveSelection || string.IsNullOrWhiteSpace(SelectedItemId))
                {
                    SelectFirstItem();
                }
                else
                {
                    EnsureSelectedItemExists();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }
    }

    public async Task MoveSelectionAsync(int direction)
    {
        var all = FlattenItems();
        if (all.Count == 0)
        {
            return;
        }

        var currentIndex = all.FindIndex(item => item.Id == SelectedItemId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }
        else
        {
            currentIndex = Math.Clamp(currentIndex + direction, 0, all.Count - 1);
        }

        SetSelection(all[currentIndex].Id);
        await Task.CompletedTask;
    }

    public Task ActivateSelectionAsync()
    {
        var selected = FlattenItems().FirstOrDefault(item => item.Id == SelectedItemId);
        return selected == null ? Task.CompletedTask : ActivateItemAsync(selected);
    }

    private async Task ActivateItemAsync(GlobalSearchItemViewModel item)
    {
        if (item.Source.OnNavigateAsync != null)
        {
            await item.Source.OnNavigateAsync().ConfigureAwait(false);
            CloseAction?.Invoke();
            return;
        }

        var target = item.Source.NavigationTarget;
        if (!string.IsNullOrWhiteSpace(target?.Route))
        {
            _navigationService.NavigateTo(target.Route, target.Parameter);
            CloseAction?.Invoke();
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.Source.Href))
        {
            _navigationService.NavigateTo(item.Source.Href);
            CloseAction?.Invoke();
        }
    }

    private void SelectFirstItem()
    {
        var first = FlattenItems().FirstOrDefault();
        SetSelection(first?.Id);
    }

    private void EnsureSelectedItemExists()
    {
        if (string.IsNullOrWhiteSpace(SelectedItemId))
        {
            SelectFirstItem();
            return;
        }

        var exists = FlattenItems().Any(item => item.Id == SelectedItemId);
        if (!exists)
        {
            SelectFirstItem();
            return;
        }

        SetSelection(SelectedItemId);
    }

    private List<GlobalSearchItemViewModel> FlattenItems() =>
        BestMatches.Concat(Groups.SelectMany(group => group.Items)).ToList();

    private void SetSelection(string? id)
    {
        SelectedItemId = id;
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                item.IsSelected = !string.IsNullOrWhiteSpace(id) && item.Id == id;
            }
        }
    }
}

public sealed partial class GlobalSearchGroupViewModel : ObservableObject
{
    public string GroupKey { get; }
    public string GroupDisplayName { get; }
    public int TotalMatched { get; }
    public bool HasMore { get; }
    public ObservableCollection<GlobalSearchItemViewModel> Items { get; }

    public GlobalSearchGroupViewModel(
        string groupKey,
        string groupDisplayName,
        int totalMatched,
        bool hasMore,
        IReadOnlyList<GlobalSearchItemViewModel> items)
    {
        GroupKey = groupKey;
        GroupDisplayName = groupDisplayName;
        TotalMatched = totalMatched;
        HasMore = hasMore;
        Items = new ObservableCollection<GlobalSearchItemViewModel>(items);
    }
}

public sealed partial class GlobalSearchItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private bool _isSelected;

    public SearchResultItem Source { get; }
    public string Id => Source.Id;
    public string Title => Source.Title;
    public string? Subtitle => Source.Subtitle;
    public string? Preview => Source.Preview;
    public string IconPath => GetIconPath(Source.Type);
    public string TypeLabel => GetTypeLabel(Source.Type);
    public bool HasTypeLabel => !string.IsNullOrWhiteSpace(TypeLabel);

    public GlobalSearchItemViewModel(SearchResultItem source, ILocalizationService localization)
    {
        Source = source;
        _localization = localization;
    }

    private static string GetIconPath(SearchResultType type) => type switch
    {
        SearchResultType.Deck => "avares://Mnemo.UI/Icons/Sidebar/flashcard.svg",
        SearchResultType.DeckCardSummary => "avares://Mnemo.UI/Icons/Sidebar/flashcard.svg",
        SearchResultType.Flashcard => "avares://Mnemo.UI/Icons/Common/list-details.svg",
        SearchResultType.Note => "avares://Mnemo.UI/Icons/Sidebar/notes.svg",
        SearchResultType.Mindmap => "avares://Mnemo.UI/Icons/Sidebar/mindmap.svg",
        SearchResultType.Setting => "avares://Mnemo.UI/Icons/Sidebar/settings.svg",
        SearchResultType.Navigation => "avares://Mnemo.UI/Icons/Common/layout.svg",
        _ => "avares://Mnemo.UI/Icons/Common/search.svg"
    };

    private string GetTypeLabel(SearchResultType type) => type switch
    {
        SearchResultType.Deck => _localization.T("TypeDeck", "GlobalSearch"),
        SearchResultType.DeckCardSummary => _localization.T("TypeDeck", "GlobalSearch"),
        SearchResultType.Flashcard => _localization.T("TypeCard", "GlobalSearch"),
        SearchResultType.Note => _localization.T("TypeNote", "GlobalSearch"),
        SearchResultType.Mindmap => _localization.T("TypeMindmap", "GlobalSearch"),
        SearchResultType.Setting => _localization.T("TypeSetting", "GlobalSearch"),
        SearchResultType.Navigation => _localization.T("TypePage", "GlobalSearch"),
        _ => string.Empty
    };
}
