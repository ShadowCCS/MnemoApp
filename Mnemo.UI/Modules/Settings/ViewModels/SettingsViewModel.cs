using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.AI;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public const string DeveloperModeKey = "App.DeveloperMode";
    public const string DeveloperModeGateUnlockedKey = "App.DeveloperModeGateUnlocked";

    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly DatasetExporter _datasetExporter;
    private readonly IChatHistoryClearService _chatHistoryClearService;
    private readonly IOverlayService _overlayService;

    private bool _developerGateUnlocked;
    private bool _developerMode;
    private int _secretTitleTapCount;
    private DateTime _lastSecretTitleTapUtc;
    private bool _settingsHandlersAttached;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategorySubtitleText))]
    private SettingsCategoryViewModel? _selectedCategory;

    [ObservableProperty]
    private string _userName = "John Doe";

    [ObservableProperty]
    private string _profilePicturePath = "avares://Mnemo.UI/Assets/ProfilePictures/img2.png";

    public ObservableCollection<SettingsCategoryViewModel> Categories { get; } = new();

    /// <summary>Subtitle under the category title; default blurb when the category has no custom subtitle.</summary>
    public string CategorySubtitleText =>
        !string.IsNullOrEmpty(SelectedCategory?.Subtitle)
            ? SelectedCategory!.Subtitle!
            : T("CategoryDescription");

    [RelayCommand]
    private void SelectCategory(SettingsCategoryViewModel category)
    {
        if (SelectedCategory != null) SelectedCategory.IsSelected = false;
        SelectedCategory = category;
        SelectedCategory.IsSelected = true;
    }

    [RelayCommand]
    private void SecretSettingsTitleTap()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSecretTitleTapUtc).TotalSeconds > 2)
            _secretTitleTapCount = 0;
        _lastSecretTitleTapUtc = now;
        _secretTitleTapCount++;
        if (_secretTitleTapCount < 7)
            return;
        _secretTitleTapCount = 0;
        _ = UnlockDeveloperGateAsync();
    }

    private async Task UnlockDeveloperGateAsync()
    {
        if (_developerGateUnlocked)
            return;
        await _settingsService.SetAsync(DeveloperModeGateUnlockedKey, true).ConfigureAwait(false);
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        ILocalizationService localizationService,
        DatasetExporter datasetExporter,
        IChatHistoryClearService chatHistoryClearService,
        IOverlayService overlayService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _localizationService = localizationService;
        _datasetExporter = datasetExporter;
        _chatHistoryClearService = chatHistoryClearService;
        _overlayService = overlayService;

        AttachSettingsHandlers();
        _ = LoadInitialSettingsAsync();

        _localizationService.LanguageChanged += OnLanguageChanged;
        RebuildCategories();
    }

    private void AttachSettingsHandlers()
    {
        if (_settingsHandlersAttached)
            return;
        _settingsHandlersAttached = true;
        _settingsService.SettingChanged += OnSettingChanged;
    }

    private async void OnSettingChanged(object? sender, string key)
    {
        if (key is "User.DisplayName" or "User.ProfilePicture")
        {
            await LoadUserProfileAsync().ConfigureAwait(false);
            return;
        }

        if (key is DeveloperModeKey or DeveloperModeGateUnlockedKey)
        {
            await RefreshDeveloperFlagsAndRebuildAsync().ConfigureAwait(false);
        }
    }

    private async Task RefreshDeveloperFlagsAndRebuildAsync()
    {
        _developerGateUnlocked = await _settingsService.GetAsync(DeveloperModeGateUnlockedKey, false).ConfigureAwait(false);
        _developerMode = await _settingsService.GetAsync(DeveloperModeKey, false).ConfigureAwait(false);
        var id = SelectedCategory?.CategoryId;
        RebuildCategories(id);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var selectedId = SelectedCategory?.CategoryId;
        RebuildCategories(selectedId);
        OnPropertyChanged(nameof(CategorySubtitleText));
    }

    private async Task LoadInitialSettingsAsync()
    {
        await LoadUserProfileAsync().ConfigureAwait(false);
        _developerGateUnlocked = await _settingsService.GetAsync(DeveloperModeGateUnlockedKey, false).ConfigureAwait(false);
        _developerMode = await _settingsService.GetAsync(DeveloperModeKey, false).ConfigureAwait(false);
        RebuildCategories(SelectedCategory?.CategoryId);
    }

    private async Task LoadUserProfileAsync()
    {
        UserName = await _settingsService.GetAsync("User.DisplayName", "John Doe").ConfigureAwait(false);
        ProfilePicturePath = await _settingsService.GetAsync("User.ProfilePicture", "avares://Mnemo.UI/Assets/ProfilePictures/img2.png").ConfigureAwait(false);
    }

    private string T(string key) => _localizationService.T(key, "Settings");

    private void RebuildCategories(string? preserveCategoryId = null)
    {
        var account = new SettingsCategoryViewModel(T("Account"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/user.svg", "Account");
        var profileGroup = new SettingsGroupViewModel(T("Profile"));
        profileGroup.Items.Add(new ProfilePictureSettingViewModel(_settingsService, T("ProfilePicture"), T("ProfilePictureDescription")));
        profileGroup.Items.Add(new NameSettingViewModel(_settingsService, T("DisplayName"), T("DisplayNameDescription")));
        account.Groups.Add(profileGroup);

        var general = new SettingsCategoryViewModel(T("General"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/settings.svg", "General") { IsSelected = true };

        var appGroup = new SettingsGroupViewModel(T("Application"));
        appGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "App.LaunchAtStartup", T("LaunchAtStartup"), T("LaunchAtStartupDescription")));
        appGroup.Items.Add(new LanguageSettingViewModel(_localizationService, _settingsService));
        appGroup.Items.Add(new ActionSettingViewModel(T("ClearCache"), T("ClearCacheDescription"), T("ClearNow")));

        var expGroup = new SettingsGroupViewModel(T("Experience"));
        expGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "App.EnableGamification", T("EnableGamification"), T("EnableGamificationDescription"), true));
        if (_developerGateUnlocked)
            expGroup.Items.Add(new ToggleSettingViewModel(_settingsService, DeveloperModeKey, "Developer mode", "Shows a Developer section in Settings. Tap the Settings title seven times within two seconds to reveal this switch."));

        general.Groups.Add(appGroup);
        general.Groups.Add(expGroup);

        var editor = new SettingsCategoryViewModel(T("Editor"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/file-description.svg", "Editor");

        var editorGroup = new SettingsGroupViewModel(T("WritingExperience"));
        editorGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Editor.AutoSave", T("AutoSave"), T("AutoSaveDescription"), true));
        editorGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Editor.SpellCheck", T("SpellCheck"), T("SpellCheckDescription"), true));
        editorGroup.Items.Add(new StepSliderSettingViewModel(_settingsService, "Editor.Width", T("EditorWidth"), T("EditorWidthDescription"), new[] { T("SuperCompact"), T("Compact"), T("Wide"), T("SuperWide") }, T("Wide")));

        var markdownGroup = new SettingsGroupViewModel(T("MarkdownRendering"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.BlockSpacing", T("BlockSpacing"), T("BlockSpacingDescription"), new[] { T("Normal"), T("Compact"), T("Relaxed") }));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.LineHeight", T("LineSpacing"), T("LineSpacingDescription"), new[] { "1.0", "1.2", "1.4", "1.45", "1.5", "1.6", "1.8", "2.0" }, null, "1.5"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.LetterSpacing", T("LetterSpacing"), T("LetterSpacingDescription"), new[] { "0", "0.2", "0.3", "0.4", "0.5", "0.8", "1.0", "1.5" }, null, "0.3"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.FontSize", T("BaseFontSize"), T("BaseFontSizeDescription"), new[] { "12px", "13px", "14px", "15px", "16px", "17px", "18px" }, null, "16px"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.CodeFontSize", T("CodeFontSize"), T("CodeFontSizeDescription"), new[] { "12px", "13px", "14px", "15px", "16px" }, null, "16px"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.MathFontSize", T("MathFontSize"), T("MathFontSizeDescription"), new[] { "14px", "16px", "18px", "20px" }, null, "16px"));
        markdownGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Markdown.RenderMath", T("RenderLatexMath"), T("RenderLatexMathDescription"), true));

        editor.Groups.Add(editorGroup);
        editor.Groups.Add(markdownGroup);

        var aiTools = new SettingsCategoryViewModel(T("AITools"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/chart-bubble.svg", "AITools");

        var aiGroup = new SettingsGroupViewModel(T("Intelligence"));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.EnableAssistant", T("EnableAIAssistant"), T("EnableAIAssistantDescription"), true));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Chat.WipeInputForDictation", T("WipeInputForDictation"), T("WipeInputForDictationDescription"), false));
        aiGroup.Items.Add(new DropdownSettingViewModel(
            _settingsService,
            "Chat.StreamingReveal",
            T("ChatStreamingReveal"),
            T("ChatStreamingRevealDescription"),
            new[] { "instant", "balanced", "smooth" },
            new[] { T("StreamingInstant"), T("StreamingBalanced"), T("StreamingSmooth") },
            "balanced"));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.SmartUnitGeneration", T("SmartUnitGeneration"), T("SmartUnitGenerationDescription")));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.GpuAcceleration", T("GpuAcceleration"), T("GpuAccelerationDescription")));
        aiGroup.Items.Add(new DropdownSettingViewModel(
            _settingsService,
            "AI.UnloadTimeout",
            T("UnloadTimeout"),
            T("UnloadTimeoutDescription"),
            new[] { UnloadTimeoutPolicy.Never, UnloadTimeoutPolicy.FiveMinutes, UnloadTimeoutPolicy.FifteenMinutes, UnloadTimeoutPolicy.OneHour },
            new[] { T("Never"), T("FiveMinutes"), T("FifteenMinutes"), T("OneHour") },
            UnloadTimeoutPolicy.FifteenMinutes,
            UnloadTimeoutPolicy.TryNormalizeToCanonicalKey));
        var clearChatLabel = T("ClearAllChatHistory");
        aiGroup.Items.Add(new AsyncActionSettingViewModel(
            T("ClearChatHistory"),
            T("ClearChatHistoryDescription"),
            clearChatLabel,
            async vm =>
            {
                var confirm = await _overlayService.CreateDialogAsync(
                    T("ClearChatHistoryConfirmTitle"),
                    T("ClearChatHistoryConfirmMessage"),
                    clearChatLabel,
                    _localizationService.T("Cancel", "Common"));
                if (confirm != clearChatLabel)
                    return;
                var result = await _chatHistoryClearService.ClearAllAsync();
                vm.StatusText = result.IsSuccess
                    ? T("ClearChatHistoryDone")
                    : result.ErrorMessage ?? "Failed";
            }));

        var ragGroup = new SettingsGroupViewModel(T("LocalKnowledge"));
        ragGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.EnableRAG", T("EnableRAG"), T("EnableRAGDescription"), true));
        ragGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "AI.EmbeddingModel", T("EmbeddingModel"), T("EmbeddingModelDescription"), new[] { T("BgeSmallFast") }));

        aiTools.Groups.Add(aiGroup);
        aiTools.Groups.Add(ragGroup);

        var appearance = new SettingsCategoryViewModel(T("Appearance"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/template.svg", "Appearance");

        var themeGroup = new SettingsGroupViewModel(T("ThemeVisuals"));
        themeGroup.Items.Add(new ThemeSettingViewModel(_themeService, T("AppTheme"), T("AppThemeDescription")));
        themeGroup.Items.Add(new AppIconSettingViewModel(_settingsService, T("AppIcon"), T("AppIconDescription")));

        appearance.Groups.Add(themeGroup);

        var mindmap = new SettingsCategoryViewModel(T("Mindmap"), "avares://Mnemo.UI/Icons/Tabler/Used/Filled/sitemap.svg", "Mindmap");

        var gridGroup = new SettingsGroupViewModel(T("GridBackground"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridType", T("GridType"), T("GridTypeDescription"), new[] { "None", "Dotted", "Lines" }, null, "Dotted"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridSize", T("GridSize"), T("GridSizeDescription"), new[] { "20", "40", "60", "80", "100" }, null, "40"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridDotSize", T("GridDotSize"), T("GridDotSizeDescription"), new[] { "0.5", "1.0", "1.5", "2.0", "2.5", "3.0" }, null, "1.5"));
        gridGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.GridOpacity", T("GridOpacity"), T("GridOpacityDescription"), new[] { "0.1", "0.2", "0.3", "0.4", "0.5", "0.6", "0.8", "1.0" }, null, "0.2"));

        var behaviourGroup = new SettingsGroupViewModel(T("Interaction"));
        behaviourGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.MinimapVisibility", T("ShowMinimap"), T("ShowMinimapDescription"), new[] { "Auto", "On", "Off" }, null, "Auto"));
        behaviourGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Mindmap.ModifierBehaviour", T("ShiftBehaviour"), T("ShiftBehaviourDescription"), new[] { T("Selecting"), T("Panning") }, null, T("Selecting")));

        mindmap.Groups.Add(gridGroup);
        mindmap.Groups.Add(behaviourGroup);

        var hotkeys = new SettingsCategoryViewModel(T("Hotkeys"), "avares://Mnemo.UI/Icons/Tabler/Used/Outlined/link.svg", "Hotkeys");
        var hotkeysGroup = new SettingsGroupViewModel(T("Shortcuts"));
        hotkeysGroup.Items.Add(new ActionSettingViewModel(T("GlobalQuickActions"), T("GlobalQuickActionsDescription"), T("ChangeBind")));
        hotkeysGroup.Items.Add(new ActionSettingViewModel(T("NewNote"), T("NewNoteDescription"), T("ChangeBind")));
        hotkeys.Groups.Add(hotkeysGroup);

        Categories.Clear();
        Categories.Add(account);
        Categories.Add(general);
        Categories.Add(editor);
        Categories.Add(aiTools);
        Categories.Add(mindmap);
        Categories.Add(appearance);
        Categories.Add(hotkeys);

        if (_developerMode)
        {
            var developer = new SettingsCategoryViewModel("Developer", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/layout.svg", "Developer")
            {
                Subtitle = "Internal tools and experimental options for development builds."
            };
            var devGroup = new SettingsGroupViewModel("Developer tools");
            devGroup.Items.Add(new SettingsNoticeViewModel("Reserved for developers", "This page holds developer-only preferences and diagnostics. More options will appear here over time."));
            devGroup.Items.Add(new ToggleSettingViewModel(_settingsService, ChatDatasetSettings.LoggingEnabledKey, "Log conversations for dataset", "Append each turn (manager model + chat model request/response) as one JSON object per line to %LocalAppData%\\mnemo\\chat_dataset\\conversations.jsonl. Off by default.", false));
            devGroup.Items.Add(new AsyncActionSettingViewModel(
                "Export training datasets",
                "Reads conversations.jsonl and generates manager_dataset.jsonl (routing fine-tune) and main_model_dataset.jsonl (chat fine-tune) in the same folder.",
                "Export",
                async vm =>
                {
                    var result = await _datasetExporter.ExportAsync(ChatDatasetSettings.LogFilePath);
                    vm.StatusText = result.IsSuccess
                        ? $"Done — manager: {result.ManagerRowCount} rows, main model: {result.MainModelRowCount} rows, skipped: {result.SkippedTurnCount}"
                        : $"Failed: {result.ErrorMessage}";
                }));
            devGroup.Items.Add(new ToggleSettingViewModel(_settingsService, TeacherModelSettings.UseTeacherRoutingKey, "Use teacher model for routing", "When on, routing and skill classification use Vertex AI Gemini (see .temp-teacher) instead of the local manager model. Falls back to the local manager if the request fails. Off by default.", false));
            devGroup.Items.Add(new ToggleSettingViewModel(_settingsService, TeacherModelSettings.UseTeacherMainChatKey, "Use teacher model as main model", "When on, chat generation uses Gemini instead of local low/mid/high tier models. Falls back is not applied mid-stream; turn off to restore local-only behavior. Off by default.", false));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.VertexCredentialsPathKey, "Vertex service account JSON path", "Optional absolute path to a Google Cloud service account key. If empty, the GOOGLE_APPLICATION_CREDENTIALS environment variable is used."));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.ChatTemperatureKey, "Teacher: chat temperature", "Vertex generation temperature for main chat streaming and tools (0–2). Default 0.7.", TeacherModelSettings.DefaultChatTemperatureString));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.ChatMaxOutputTokensKey, "Teacher: chat max output tokens", "Max tokens for chat streaming (typical 1024–65535). Default 65536.", TeacherModelSettings.DefaultChatMaxOutputTokensString));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.RoutingTemperatureKey, "Teacher: routing temperature", "Temperature for routing JSON (often 0 for deterministic skill selection). Default 0.", TeacherModelSettings.DefaultRoutingTemperatureString));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.RoutingMaxOutputTokensKey, "Teacher: routing max output tokens", "Max tokens for routing response. Default 512.", TeacherModelSettings.DefaultRoutingMaxOutputTokensString));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.StructuredTemperatureKey, "Teacher: structured JSON temperature", "Temperature when the app forces JSON (e.g. learning path). Default 0.2.", TeacherModelSettings.DefaultStructuredTemperatureString));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.StructuredMaxOutputTokensKey, "Teacher: structured JSON max output tokens", "Max tokens for forced JSON non-streaming calls. Default 8192.", TeacherModelSettings.DefaultStructuredMaxOutputTokensString));
            devGroup.Items.Add(new TextSettingViewModel(_settingsService, TeacherModelSettings.ChatStylePromptKey, "Teacher: answer style (system suffix)", "When \"Use teacher model as main model\" is on, this text is appended to the system prompt (after skill context) to steer tone, length, headings, or question-and-answer format for dataset collection. Leave empty for default behavior.", ""));
            developer.Groups.Add(devGroup);
            Categories.Add(developer);
        }

        var targetId = preserveCategoryId;
        if (targetId == "Developer" && !_developerMode)
            targetId = "General";

        var pick = !string.IsNullOrEmpty(targetId)
            ? Categories.FirstOrDefault(c => c.CategoryId == targetId)
            : null;
        pick ??= Categories.FirstOrDefault(c => c.CategoryId == "General") ?? Categories.FirstOrDefault();

        foreach (var c in Categories)
            c.IsSelected = false;
        if (pick != null)
        {
            pick.IsSelected = true;
            SelectedCategory = pick;
        }
    }
}
