using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;

namespace Mnemo.UI.Modules.Overview.ViewModels;

/// <summary>
/// ViewModel for the Add Widget overlay.
/// Shows available widgets and adds the selected one to the dashboard at the first available position.
/// </summary>
public partial class AddWidgetViewModel : ObservableObject
{
    private readonly IWidgetRegistry _widgetRegistry;
    private readonly IOverlayService _overlayService;
    private readonly OverviewViewModel _overviewViewModel;
    private readonly ILocalizationService _localization;
    private readonly List<WidgetMetadata> _allWidgets = [];

    /// <summary>
    /// Gets or sets the overlay identifier used to close this overlay.
    /// </summary>
    public string OverlayId { get; set; } = string.Empty;

    /// <summary>
    /// Gallery rows after search and category filter.
    /// </summary>
    public ObservableCollection<WidgetGalleryDisplayItem> FilteredWidgets { get; } = new();

    /// <summary>
    /// Total widgets registered (footer denominator).
    /// </summary>
    public int TotalWidgetCount => _allWidgets.Count;

    /// <summary>
    /// Search query for filtering gallery rows.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// Selected gallery filter key: All, Study, Flashcards, Productivity, Community.
    /// </summary>
    [ObservableProperty]
    private string _selectedGalleryFilterKey = "All";

    public bool IsAllFilterSelected => SelectedGalleryFilterKey == "All";
    public bool IsStudyFilterSelected => SelectedGalleryFilterKey == nameof(WidgetGalleryFilterCategory.Study);
    public bool IsFlashcardsFilterSelected => SelectedGalleryFilterKey == nameof(WidgetGalleryFilterCategory.Flashcards);
    public bool IsProductivityFilterSelected => SelectedGalleryFilterKey == nameof(WidgetGalleryFilterCategory.Productivity);
    public bool IsCommunityFilterSelected => SelectedGalleryFilterKey == nameof(WidgetGalleryFilterCategory.Community);

    /// <summary>
    /// Footer text such as "4 of 5 widgets".
    /// </summary>
    public string WidgetCountFooter
    {
        get
        {
            var fmt = _localization.T("FooterCountFormat", "AddWidget");
            return string.Format(CultureInfo.CurrentCulture, fmt, FilteredWidgets.Count, TotalWidgetCount);
        }
    }

    public AddWidgetViewModel(
        IWidgetRegistry widgetRegistry,
        IOverlayService overlayService,
        OverviewViewModel overviewViewModel,
        ILocalizationService localizationService)
    {
        _widgetRegistry = widgetRegistry;
        _overlayService = overlayService;
        _overviewViewModel = overviewViewModel;
        _localization = localizationService;

        foreach (var widget in _widgetRegistry.GetAllWidgets())
            _allWidgets.Add(widget.Metadata);

        _localization.LanguageChanged += OnLanguageChanged;
        _overviewViewModel.Widgets.CollectionChanged += OnDashboardWidgetsChanged;
        ApplyFilter();
    }

    /// <summary>
    /// Stops listening for language changes (call when the overlay is closed or the view unloads).
    /// </summary>
    public void DetachLocalizationListener()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _overviewViewModel.Widgets.CollectionChanged -= OnDashboardWidgetsChanged;
    }

    private void OnDashboardWidgetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyFilter();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        NotifyFilterChipBindings();
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedGalleryFilterKeyChanged(string value)
    {
        NotifyFilterChipBindings();
        ApplyFilter();
    }

    private void NotifyFilterChipBindings()
    {
        OnPropertyChanged(nameof(IsAllFilterSelected));
        OnPropertyChanged(nameof(IsStudyFilterSelected));
        OnPropertyChanged(nameof(IsFlashcardsFilterSelected));
        OnPropertyChanged(nameof(IsProductivityFilterSelected));
        OnPropertyChanged(nameof(IsCommunityFilterSelected));
    }

    [RelayCommand]
    private void SelectGalleryFilter(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        SelectedGalleryFilterKey = key;
    }

    private void ApplyFilter()
    {
        var query = SearchQuery.Trim();
        var comparer = CultureInfo.CurrentCulture.CompareInfo;
        var opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        IEnumerable<WidgetMetadata> source = _allWidgets;

        if (SelectedGalleryFilterKey != "All" &&
            Enum.TryParse<WidgetGalleryFilterCategory>(SelectedGalleryFilterKey, out var cat))
        {
            source = source.Where(w => w.GalleryFilter == cat);
        }

        var rows = source
            .Select(CreateDisplayItem)
            .Where(item =>
                string.IsNullOrEmpty(query) ||
                comparer.IndexOf(item.SearchBlob, query, opts) >= 0)
            .OrderByDescending(i => i.Metadata.IsFeatured)
            .ThenBy(i => GetSortTitle(i.Metadata), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FilteredWidgets.Clear();
        foreach (var r in rows)
            FilteredWidgets.Add(r);

        OnPropertyChanged(nameof(WidgetCountFooter));
    }

    private string GetSortTitle(WidgetMetadata m) => ResolveTitle(m);

    private WidgetGalleryDisplayItem CreateDisplayItem(WidgetMetadata m)
    {
        var metaLine = BuildMetaLine(m);
        var desc = ResolveGalleryDescription(m);
        var tags = ResolveTagLabels(m);
        var searchBlob = string.Join('\u00A0', new[]
        {
            ResolveTitle(m),
            desc,
            metaLine,
            string.Join(' ', tags),
        });

        var onDashboard = _overviewViewModel.Widgets.Any(w => w.WidgetId == m.Id);
        return new WidgetGalleryDisplayItem(
            m,
            ResolveTitle(m),
            metaLine,
            desc,
            tags,
            searchBlob,
            onDashboard,
            _localization.T("WidgetGalleryAdd", "AddWidget"),
            _localization.T("WidgetGalleryAdded", "AddWidget"),
            _localization.T("WidgetGalleryRemove", "AddWidget"));
    }

    private string BuildMetaLine(WidgetMetadata m)
    {
        var cat = _localization.T($"Filter{m.GalleryFilter}", "AddWidget");
        var product = _localization.T(m.ProductLocalizationKey, "AddWidget");
        return $"{cat} · {product} · {m.Version}";
    }

    private string ResolveTitle(WidgetMetadata m)
    {
        if (!string.IsNullOrEmpty(m.TranslationNamespace))
        {
            var t = _localization.T("Title", m.TranslationNamespace);
            if (t != "Title")
                return t;
        }

        return m.Title;
    }

    private string ResolveGalleryDescription(WidgetMetadata m)
    {
        if (!string.IsNullOrEmpty(m.TranslationNamespace))
        {
            var g = _localization.T("GalleryDescription", m.TranslationNamespace);
            if (g != "GalleryDescription")
                return g;
            var d = _localization.T("Description", m.TranslationNamespace);
            if (d != "Description")
                return d;
        }

        return m.Description;
    }

    private List<string> ResolveTagLabels(WidgetMetadata m)
    {
        var list = new List<string>();
        foreach (var key in m.GalleryTagKeys)
        {
            if (!string.IsNullOrEmpty(m.TranslationNamespace))
            {
                var t = _localization.T(key, m.TranslationNamespace);
                list.Add(t != key ? t : key);
            }
            else
            {
                list.Add(key);
            }
        }

        return list;
    }

    /// <summary>
    /// Adds the widget if missing, or removes it when already on the dashboard. Overlay stays open.
    /// </summary>
    [RelayCommand]
    private async Task ToggleWidgetOnDashboard(WidgetMetadata? widget)
    {
        if (widget == null)
            return;

        var existing = _overviewViewModel.Widgets.FirstOrDefault(w => w.WidgetId == widget.Id);
        if (existing != null)
            _overviewViewModel.RemoveWidget(existing);
        else
            await _overviewViewModel.AddWidgetAsync(widget.Id, new WidgetPosition(0, 0));
    }

    /// <summary>
    /// Cancels adding a widget and closes the overlay.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        DetachLocalizationListener();
        _overlayService.CloseOverlay(OverlayId);
    }
}
