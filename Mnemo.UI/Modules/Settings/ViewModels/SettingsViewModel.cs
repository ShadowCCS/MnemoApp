using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private string _userName = "John Doe";

    [ObservableProperty]
    private string _profilePicturePath = "avares://Mnemo.UI/Assets/ProfilePictures/img2.png";

    [ObservableProperty]
    private SettingsCategoryViewModel? _selectedCategory;

    public ObservableCollection<SettingsCategoryViewModel> Categories { get; } = new();

    [RelayCommand]
    private void SelectCategory(SettingsCategoryViewModel category)
    {
        if (SelectedCategory != null) SelectedCategory.IsSelected = false;
        SelectedCategory = category;
        SelectedCategory.IsSelected = true;
    }

    public SettingsViewModel(ISettingsService settingsService, IThemeService themeService, ILocalizationService localizationService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _localizationService = localizationService;
        
        // Load settings asynchronously
        _ = LoadInitialSettingsAsync();

        // Listen for changes to update sidebar profile info
        _settingsService.SettingChanged += (s, e) =>
        {
            if (e == "User.DisplayName" || e == "User.ProfilePicture")
            {
                _ = LoadInitialSettingsAsync();
            }
        };

        _localizationService.LanguageChanged += OnLanguageChanged;
        InitializeCategories();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var selectedId = SelectedCategory?.CategoryId;
        Categories.Clear();
        InitializeCategories();
        if (!string.IsNullOrEmpty(selectedId))
            SelectedCategory = Categories.FirstOrDefault(c => c.CategoryId == selectedId) ?? Categories.FirstOrDefault();
    }

    private async Task LoadInitialSettingsAsync()
    {
        UserName = await _settingsService.GetAsync("User.DisplayName", "John Doe");
        ProfilePicturePath = await _settingsService.GetAsync("User.ProfilePicture", "avares://Mnemo.UI/Assets/ProfilePictures/img2.png");
    }

    private void InitializeCategories()
    {
        string T(string key) => _localizationService.T(key, "Settings");

        // Account Category
        var account = new SettingsCategoryViewModel(T("Account"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/user.svg", "Account");
        var profileGroup = new SettingsGroupViewModel(T("Profile"));
        profileGroup.Items.Add(new ProfilePictureSettingViewModel(_settingsService, T("ProfilePicture"), T("ProfilePictureDescription")));
        profileGroup.Items.Add(new NameSettingViewModel(_settingsService, T("DisplayName"), T("DisplayNameDescription")));
        account.Groups.Add(profileGroup);

        // General Category
        var general = new SettingsCategoryViewModel(T("General"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/settings.svg", "General") { IsSelected = true };

        var appGroup = new SettingsGroupViewModel(T("Application"));
        appGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "App.LaunchAtStartup", T("LaunchAtStartup"), T("LaunchAtStartupDescription")));
        appGroup.Items.Add(new LanguageSettingViewModel(_localizationService, _settingsService));
        appGroup.Items.Add(new ActionSettingViewModel(T("ClearCache"), T("ClearCacheDescription"), T("ClearNow")));

        var expGroup = new SettingsGroupViewModel(T("Experience"));
        expGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "App.EnableGamification", T("EnableGamification"), T("EnableGamificationDescription"), true));

        general.Groups.Add(appGroup);
        general.Groups.Add(expGroup);

        // Editor Category
        var editor = new SettingsCategoryViewModel(T("Editor"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/file-description.svg", "Editor");

        var editorGroup = new SettingsGroupViewModel(T("WritingExperience"));
        editorGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Editor.AutoSave", T("AutoSave"), T("AutoSaveDescription"), true));
        editorGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Editor.SpellCheck", T("SpellCheck"), T("SpellCheckDescription"), true));
        editorGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Editor.DefaultView", T("DefaultView"), T("DefaultViewDescription"), new[] { T("EditorOnly"), T("PreviewOnly"), T("SplitView") }));

        var markdownGroup = new SettingsGroupViewModel(T("MarkdownRendering"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.BlockSpacing", T("BlockSpacing"), T("BlockSpacingDescription"), new[] { T("Normal"), T("Compact"), T("Relaxed") }));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.LineHeight", T("LineSpacing"), T("LineSpacingDescription"), new[] { "1.0", "1.2", "1.4", "1.45", "1.5", "1.6", "1.8", "2.0" }, "1.5"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.LetterSpacing", T("LetterSpacing"), T("LetterSpacingDescription"), new[] { "0", "0.2", "0.3", "0.4", "0.5", "0.8", "1.0", "1.5" }, "0.3"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.FontSize", T("BaseFontSize"), T("BaseFontSizeDescription"), new[] { "12px", "13px", "14px", "15px", "16px", "17px", "18px" }, "14px"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.CodeFontSize", T("CodeFontSize"), T("CodeFontSizeDescription"), new[] { "12px", "13px", "14px", "15px", "16px" }, "15px"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.MathFontSize", T("MathFontSize"), T("MathFontSizeDescription"), new[] { "14px", "16px", "18px", "20px" }, "14px"));
        markdownGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Markdown.RenderMath", T("RenderLatexMath"), T("RenderLatexMathDescription"), true));

        editor.Groups.Add(editorGroup);
        editor.Groups.Add(markdownGroup);

        // AI & Tools Category
        var aiTools = new SettingsCategoryViewModel(T("AITools"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/chart-bubble.svg", "AITools");

        var aiGroup = new SettingsGroupViewModel(T("Intelligence"));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.EnableAssistant", T("EnableAIAssistant"), T("EnableAIAssistantDescription"), true));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Chat.WipeInputForDictation", T("WipeInputForDictation"), T("WipeInputForDictationDescription"), false));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.SmartSwitch", T("SmartSwitch"), T("SmartSwitchDescription")));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.SmartUnitGeneration", T("SmartUnitGeneration"), T("SmartUnitGenerationDescription")));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.GpuAcceleration", T("GpuAcceleration"), T("GpuAccelerationDescription")));
        aiGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "AI.UnloadTimeout", T("UnloadTimeout"), T("UnloadTimeoutDescription"), new[] { T("Never"), T("FiveMinutes"), T("FifteenMinutes"), T("OneHour") }));

        var ragGroup = new SettingsGroupViewModel(T("LocalKnowledge"));
        ragGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.EnableRAG", T("EnableRAG"), T("EnableRAGDescription"), true));
        ragGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "AI.EmbeddingModel", T("EmbeddingModel"), T("EmbeddingModelDescription"), new[] { T("BgeSmallFast") }));

        aiTools.Groups.Add(aiGroup);
        aiTools.Groups.Add(ragGroup);

        // Appearance Category
        var appearance = new SettingsCategoryViewModel(T("Appearance"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/template.svg", "Appearance");

        var themeGroup = new SettingsGroupViewModel(T("ThemeVisuals"));
        themeGroup.Items.Add(new ThemeSettingViewModel(_themeService, T("AppTheme"), T("AppThemeDescription")));
        themeGroup.Items.Add(new AppIconSettingViewModel(_settingsService, T("AppIcon"), T("AppIconDescription")));

        appearance.Groups.Add(themeGroup);

        // Mindmap Category
        var mindmap = new SettingsCategoryViewModel(T("Mindmap"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/sitemap.svg", "Mindmap");

        var gridGroup = new SettingsGroupViewModel(T("GridBackground"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridType", T("GridType"), T("GridTypeDescription"), new[] { "None", "Dotted", "Lines" }, "Dotted"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridSize", T("GridSize"), T("GridSizeDescription"), new[] { "20", "40", "60", "80", "100" }, "40"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridDotSize", T("GridDotSize"), T("GridDotSizeDescription"), new[] { "0.5", "1.0", "1.5", "2.0", "2.5", "3.0" }, "1.5"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridOpacity", T("GridOpacity"), T("GridOpacityDescription"), new[] { "0.1", "0.2", "0.3", "0.4", "0.5", "0.6", "0.8", "1.0" }, "0.2"));

        var behaviourGroup = new SettingsGroupViewModel(T("Interaction"));
        behaviourGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.ModifierBehaviour", T("ShiftBehaviour"), T("ShiftBehaviourDescription"), new[] { T("Selecting"), T("Panning") }, T("Selecting")));

        mindmap.Groups.Add(gridGroup);
        mindmap.Groups.Add(behaviourGroup);

        // Hotkeys Category
        var hotkeys = new SettingsCategoryViewModel(T("Hotkeys"), "avares://Mnemo.UI/Icons/Tabler/Used/Outlined/link.svg", "Hotkeys");
        var hotkeysGroup = new SettingsGroupViewModel(T("Shortcuts"));
        hotkeysGroup.Items.Add(new ActionSettingViewModel(T("GlobalQuickActions"), T("GlobalQuickActionsDescription"), T("ChangeBind")));
        hotkeysGroup.Items.Add(new ActionSettingViewModel(T("NewNote"), T("NewNoteDescription"), T("ChangeBind")));
        hotkeys.Groups.Add(hotkeysGroup);

        Categories.Add(account);
        Categories.Add(general);
        Categories.Add(editor);
        Categories.Add(aiTools);
        Categories.Add(mindmap);
        Categories.Add(appearance);
        Categories.Add(hotkeys);

        SelectedCategory = general;
    }
}
