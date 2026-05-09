using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Keybinds;
using Mnemo.UI.Modules.Settings.ViewModels;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components.Overlays;

public partial class KeybindManagerOverlayViewModel : ViewModelBase
{
    private const string KeybindNs = "Keybinds";

    private readonly IKeyMap _keyMap;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private bool _rebuilding;
    private bool _refreshingModuleFilter;

    public ObservableCollection<KeybindModuleSectionVm> ModuleSections { get; } = new();
    public ObservableCollection<KeybindConflictRowVm> Conflicts { get; } = new();
    public ObservableCollection<KeybindModuleFilterOption> ModuleFilterOptions { get; } = new();

    [ObservableProperty]
    private bool _showDeveloperActionIds;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _showConflictsOnly;

    [ObservableProperty]
    private KeybindModuleFilterOption? _selectedModuleFilter;

    [ObservableProperty]
    private string _footerStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasConflicts;

    public string OverlayTitle => _localization.T("keybindManager.title", KeybindNs);
    public string OverlaySubtitle => _localization.T("keybindManager.subtitle", KeybindNs);
    public string SearchWatermark => _localization.T("keybindManager.searchPlaceholder", KeybindNs);
    public string ConflictsOnlyLabel => _localization.T("keybindManager.conflictsOnly", KeybindNs);
    public string ConflictsHeader => _localization.T("keybindManager.conflictsHeader", KeybindNs);
    public string CloseLabel => _localization.T("keybindManager.close", KeybindNs);
    public string ResetAllLabel => _localization.T("keybindManager.resetAll", KeybindNs);

    public KeybindManagerOverlayViewModel(IKeyMap keyMap, ILocalizationService localization, ISettingsService settings)
    {
        _keyMap = keyMap;
        _localization = localization;
        _settings = settings;
        _localization.LanguageChanged += OnLanguageChanged;
        _settings.SettingChanged += OnSettingChanged;
        RebuildAll();
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key == SettingsViewModel.DeveloperModeKey)
            _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        ShowDeveloperActionIds = await _settings.GetAsync(SettingsViewModel.DeveloperModeKey, false).ConfigureAwait(true);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RebuildAll();

    [RelayCommand]
    private async Task ResetAllAsync()
    {
        await _keyMap.ResetAllOverridesAsync().ConfigureAwait(true);
        RebuildAll();
    }

    partial void OnSearchQueryChanged(string value) => RebuildListCore();

    partial void OnShowConflictsOnlyChanged(bool value) => RebuildListCore();

    partial void OnSelectedModuleFilterChanged(KeybindModuleFilterOption? value)
    {
        if (_refreshingModuleFilter)
            return;
        RebuildListCore();
    }

    partial void OnShowDeveloperActionIdsChanged(bool value) => RebuildListCore();

    private void RebuildAll()
    {
        if (_rebuilding)
            return;
        _rebuilding = true;
        try
        {
            RefreshModuleFilterOptions();
            RebuildListCore();
            NotifyOverlayStringsChanged();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>Rebuilds the shortcut list, conflicts, and footer. Does not replace <see cref="ModuleFilterOptions"/> (ComboBox loses selection visuals if ItemsSource is cleared on every selection change).</summary>
    private void RebuildListCore()
    {
        var conflictIds = new HashSet<string>(StringComparer.Ordinal);
        Conflicts.Clear();
        foreach (var c in _keyMap.CheckConflictsAllStatic())
        {
            Conflicts.Add(new KeybindConflictRowVm(c.Severity.ToString(), c.Message, c.ActionIdA, c.ActionIdB));
            if (!string.IsNullOrEmpty(c.ActionIdA))
                conflictIds.Add(c.ActionIdA);
            if (!string.IsNullOrEmpty(c.ActionIdB))
                conflictIds.Add(c.ActionIdB);
        }

        HasConflicts = Conflicts.Count > 0;

        var allDefs = _keyMap.GetAllStaticDefinitionsMerged().ToList();
        var totalCount = allDefs.Count;

        var q = SearchQuery.Trim();
        var moduleId = SelectedModuleFilter?.ModuleId;

        IEnumerable<KeybindActionDefinition> filtered = allDefs;
        if (ShowConflictsOnly)
            filtered = filtered.Where(d => conflictIds.Contains(d.ActionId));
        if (!string.IsNullOrEmpty(moduleId))
            filtered = filtered.Where(d => string.Equals(d.Module, moduleId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(d =>
                ActionTitle(d).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.ActionId.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered
            .OrderBy(d => BuiltinModuleSortIndex(d.Module))
            .ThenBy(d => d.Module ?? "core", StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.DisplayCategoryKey ?? "")
            .ThenBy(d => d.ActionId, StringComparer.Ordinal)
            .ToList();

        ModuleSections.Clear();
        foreach (var moduleGroup in list.GroupBy(d => d.Module ?? "core"))
        {
            var moduleKey = moduleGroup.Key;
            var moduleLabel = _localization.T("module." + moduleKey, KeybindNs);
            var section = new KeybindModuleSectionVm(
                moduleLabel,
                new ObservableCollection<KeybindCategorySectionVm>());

            foreach (var catGroup in moduleGroup.GroupBy(d => d.DisplayCategoryKey ?? "category.general"))
            {
                var catKey = catGroup.Key;
                var catLabel = _localization.T(catKey, KeybindNs);
                var catSection = new KeybindCategorySectionVm(
                    catLabel,
                    new ObservableCollection<KeybindActionRowVm>());

                foreach (var def in catGroup.OrderBy(d => ActionTitle(d), StringComparer.OrdinalIgnoreCase))
                    catSection.Rows.Add(BuildRow(def));

                section.Categories.Add(catSection);
            }

            ModuleSections.Add(section);
        }

        var shown = list.Count;
        FooterStatusText = string.Format(
            _localization.T("keybindManager.footerCount", KeybindNs),
            shown,
            totalCount);
    }

    private void NotifyOverlayStringsChanged()
    {
        OnPropertyChanged(nameof(OverlayTitle));
        OnPropertyChanged(nameof(OverlaySubtitle));
        OnPropertyChanged(nameof(SearchWatermark));
        OnPropertyChanged(nameof(ConflictsOnlyLabel));
        OnPropertyChanged(nameof(ConflictsHeader));
        OnPropertyChanged(nameof(CloseLabel));
        OnPropertyChanged(nameof(ResetAllLabel));
    }

    private void RefreshModuleFilterOptions()
    {
        var all = _keyMap.GetAllStaticDefinitionsMerged();
        var distinct = all
            .Select(d => d.Module ?? "core")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => BuiltinModuleSortIndex(m))
            .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var prevId = SelectedModuleFilter?.ModuleId;
        _refreshingModuleFilter = true;
        try
        {
            ModuleFilterOptions.Clear();
            ModuleFilterOptions.Add(new KeybindModuleFilterOption(
                null,
                _localization.T("keybindManager.filterAllModules", KeybindNs)));
            foreach (var m in distinct)
            {
                ModuleFilterOptions.Add(new KeybindModuleFilterOption(
                    m,
                    _localization.T("module." + m, KeybindNs)));
            }

            SelectedModuleFilter = ModuleFilterOptions.FirstOrDefault(o => o.ModuleId == prevId)
                ?? ModuleFilterOptions[0];
        }
        finally
        {
            _refreshingModuleFilter = false;
        }
    }

    private string ActionTitle(KeybindActionDefinition def)
    {
        var labelKey = def.DisplayLabelKey ?? def.ActionId;
        return _localization.T(labelKey, KeybindNs);
    }

    private KeybindActionRowVm BuildRow(KeybindActionDefinition def)
    {
        var title = ActionTitle(def);
        string? detail = null;
        if (!string.IsNullOrEmpty(def.DisplayDescriptionKey))
        {
            var desc = _localization.T(def.DisplayDescriptionKey, KeybindNs);
            if (!string.Equals(desc, def.DisplayDescriptionKey, StringComparison.Ordinal))
                detail = desc;
        }

        var strip = new ObservableCollection<ShortcutDisplayPieceVm>();
        if (def.Bindings.Count == 0)
        {
            strip.Add(new ShortcutDisplayPieceVm(ShortcutStripPieceKind.KeyPill, "—"));
        }
        else
        {
            var thenText = _localization.T("keybindManager.then", KeybindNs);
            for (var bi = 0; bi < def.Bindings.Count; bi++)
            {
                if (bi > 0)
                    strip.Add(new ShortcutDisplayPieceVm(ShortcutStripPieceKind.AlternativeSeparator, "·"));
                foreach (var pill in KeybindGestureDisplayFormatter.FormatBindingDisplayPills(def.Bindings[bi]))
                {
                    if (pill.IsThenSeparator)
                        strip.Add(new ShortcutDisplayPieceVm(ShortcutStripPieceKind.ThenLabel, thenText));
                    else
                        strip.Add(new ShortcutDisplayPieceVm(ShortcutStripPieceKind.KeyPill, pill.Text));
                }
            }
        }

        return new KeybindActionRowVm(
            title,
            detail,
            strip,
            def.ActionId,
            def.Enabled,
            ShowDeveloperActionIds);
    }

    /// <summary>
    /// Shipped modules get a stable, readable order in the overlay. Any other <see cref="KeybindActionDefinition.Module"/>
    /// (extensions, future features) shares one tier and sorts alphabetically by module id after these.
    /// </summary>
    private static readonly string[] BuiltinModuleDisplayOrder =
    [
        "core",
        "editor",
        "mindmap",
        "flashcards",
    ];

    private static int BuiltinModuleSortIndex(string? moduleId)
    {
        var key = (moduleId ?? "core").ToLowerInvariant();
        for (var i = 0; i < BuiltinModuleDisplayOrder.Length; i++)
        {
            if (string.Equals(BuiltinModuleDisplayOrder[i], key, StringComparison.Ordinal))
                return i;
        }
        return BuiltinModuleDisplayOrder.Length;
    }
}

public sealed record KeybindModuleFilterOption(string? ModuleId, string Label)
{
    public override string ToString() => Label;
}

public sealed class KeybindModuleSectionVm(string moduleTitle, ObservableCollection<KeybindCategorySectionVm> categories)
{
    public string ModuleTitle { get; } = moduleTitle;
    public string ModuleHeaderUpper => ModuleTitle.ToUpperInvariant();
    public ObservableCollection<KeybindCategorySectionVm> Categories { get; } = categories;
}

public sealed class KeybindCategorySectionVm(string categoryTitle, ObservableCollection<KeybindActionRowVm> rows)
{
    public string CategoryTitle { get; } = categoryTitle;
    public ObservableCollection<KeybindActionRowVm> Rows { get; } = rows;
}

public sealed class KeybindActionRowVm(
    string title,
    string? detailLine,
    ObservableCollection<ShortcutDisplayPieceVm> shortcutStrip,
    string actionId,
    bool enabled,
    bool showDeveloperActionId)
{
    public string Title { get; } = title;
    public string? DetailLine { get; } = detailLine;
    public bool HasDetailLine => !string.IsNullOrEmpty(DetailLine);
    public ObservableCollection<ShortcutDisplayPieceVm> ShortcutStrip { get; } = shortcutStrip;
    public string ActionId { get; } = actionId;
    public bool Enabled { get; } = enabled;
    public bool ShowDeveloperActionId { get; } = showDeveloperActionId;
}

public enum ShortcutStripPieceKind
{
    KeyPill,
    ThenLabel,
    AlternativeSeparator,
}

public sealed class ShortcutDisplayPieceVm(ShortcutStripPieceKind kind, string text)
{
    public ShortcutStripPieceKind Kind { get; } = kind;
    public string Text { get; } = text;
    public bool IsKeyPill => Kind == ShortcutStripPieceKind.KeyPill;
    public bool IsThenLabel => Kind == ShortcutStripPieceKind.ThenLabel;
    public bool IsAlternativeSeparator => Kind == ShortcutStripPieceKind.AlternativeSeparator;
}

public sealed record KeybindConflictRowVm(string Severity, string Message, string? ActionIdA, string? ActionIdB);
