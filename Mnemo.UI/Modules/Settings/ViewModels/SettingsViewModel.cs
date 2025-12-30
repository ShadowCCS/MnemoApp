using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private string _userName = "John Doe";

    [ObservableProperty]
    private string _profilePicturePath = "avares://Mnemo.UI/Assets/demo-profile-pic.png";

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

    public SettingsViewModel(ISettingsService settingsService, IThemeService themeService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        
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

        InitializeCategories();
    }

    private async Task LoadInitialSettingsAsync()
    {
        UserName = await _settingsService.GetAsync("User.DisplayName", "John Doe");
        ProfilePicturePath = await _settingsService.GetAsync("User.ProfilePicture", "avares://Mnemo.UI/Assets/demo-profile-pic.png");
    }

    private void InitializeCategories()
    {
        // Account Category
        var account = new SettingsCategoryViewModel("Account", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/user.svg");
        var profileGroup = new SettingsGroupViewModel("Profile");
        profileGroup.Items.Add(new ProfilePictureSettingViewModel(_settingsService, "Profile Picture", "Your personal avatar shown in the app."));
        profileGroup.Items.Add(new NameSettingViewModel(_settingsService, "Display Name", "Your name will be visible to other users when you share notes."));
        account.Groups.Add(profileGroup);

        // General Category
        var general = new SettingsCategoryViewModel("General", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/settings.svg") { IsSelected = true };
        
        var appGroup = new SettingsGroupViewModel("Application");
        appGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "App.LaunchAtStartup", "Launch at Startup", "Automatically start Mnemo when you log in."));
        appGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "App.Language", "Language", "Choose your preferred language.", new[] { "English", "French", "German", "Spanish" }));
        appGroup.Items.Add(new ActionSettingViewModel("Clear Cache", "Delete all temporary files to free up space.", "Clear Now"));
        
        var expGroup = new SettingsGroupViewModel("Experience");
        expGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "App.EnableGamification", "Enable Gamification", "Track XP, streaks, and level up as you learn.", true));
        
        general.Groups.Add(appGroup);
        general.Groups.Add(expGroup);

        // Editor Category
        var editor = new SettingsCategoryViewModel("Editor", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/file-description.svg");
        
        var editorGroup = new SettingsGroupViewModel("Writing Experience");
        editorGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Editor.AutoSave", "Auto-save", "Automatically save changes every few seconds.", true));
        editorGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Editor.SpellCheck", "Spell Check", "Highlight misspelled words in the editor.", true));
        editorGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Editor.DefaultView", "Default View", "Choose the default view for new notes.", new[] { "Editor Only", "Preview Only", "Split View" }));
        
        var markdownGroup = new SettingsGroupViewModel("Markdown Rendering");
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.BlockSpacing", "Block Spacing", "Adjust the vertical space between paragraphs and sections.", new[] { "Normal", "Compact", "Relaxed" }));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.LineHeight", "Line Spacing", "Adjust the height between lines of text within a paragraph.", new[] { "1.0", "1.2", "1.4", "1.45", "1.5", "1.6", "1.8", "2.0" }, "1.5"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.LetterSpacing", "Letter Spacing", "Adjust the space between characters.", new[] { "0", "0.2", "0.3", "0.4", "0.5", "0.8", "1.0", "1.5" }, "0.3"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.FontSize", "Base Font Size", "Change the base font size for the rendered markdown.", new[] { "12px", "13px", "14px", "15px", "16px", "17px", "18px" }, "14px"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.CodeFontSize", "Code Font Size", "Adjust the font size for code blocks.", new[] { "12px", "13px", "14px", "15px", "16px" }, "15px"));
        markdownGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "Markdown.MathFontSize", "Math Font Size", "Adjust the font size for LaTeX formulas.", new[] { "14px", "16px", "18px", "20px" }, "14px"));
        markdownGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "Markdown.RenderMath", "Render LaTeX Math", "Enable or disable mathematical formula rendering.", true));
        
        editor.Groups.Add(editorGroup);
        editor.Groups.Add(markdownGroup);

        // AI & Tools Category
        var aiTools = new SettingsCategoryViewModel("AI & Tools", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/chart-bubble.svg");
        
        var aiGroup = new SettingsGroupViewModel("Intelligence");
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.EnableAssistant", "Enable AI Assistant", "Use AI to help you write, summarize, and organize.", true));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.SmartSwitch", "Smart Switch", "Automatically use the smarter (but slower) model for complex tasks."));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.SmartUnitGeneration", "Smart Unit Generation", "Generate only the first unit on creation, then others on-demand."));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.UseGemini", "Use Gemini (Experimental)", "Enable Google Gemini API for all AI tasks. Bypasses local models and router."));
        aiGroup.Items.Add(new TextSettingViewModel(_settingsService, "AI.GeminiApiKey", "Gemini API Key", "Your Google AI Studio API key.", "", true));
        aiGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.GpuAcceleration", "GPU Acceleration", "Use your graphics card to speed up AI inference. Requires NVIDIA GPU with CUDA."));
        aiGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "AI.UnloadTimeout", "Unload Timeout", "Free up memory when AI models are not in use.", new[] { "Never", "5 Minutes", "15 Minutes", "1 Hour" }));
        
        var ragGroup = new SettingsGroupViewModel("Local Knowledge");
        ragGroup.Items.Add(new ToggleSettingViewModel(_settingsService, "AI.SmartRAG", "Smart RAG", "Only search your notes when the query is substantial. Skips RAG for greetings and short questions.", true));
        ragGroup.Items.Add(new DropdownSettingViewModel(_settingsService, "AI.EmbeddingModel", "Embedding Model", "The model used to index your personal library.", new[] { "bge-small (Fast)", "nomic-embed (Accurate)" }));
        
        aiTools.Groups.Add(aiGroup);
        aiTools.Groups.Add(ragGroup);

        // Appearance Category
        var appearance = new SettingsCategoryViewModel("Appearance", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/template.svg");
        
        var themeGroup = new SettingsGroupViewModel("Theme & Visuals");
        themeGroup.Items.Add(new ThemeSettingViewModel(_themeService, "App Theme", "Select the visual style of the application."));
        themeGroup.Items.Add(new AppIconSettingViewModel(_settingsService, "App Icon", "Customize the application icon in your taskbar."));
        
        appearance.Groups.Add(themeGroup);

        // Hotkeys Category
        var hotkeys = new SettingsCategoryViewModel("Hotkeys", "avares://Mnemo.UI/Icons/Tabler/Used/Outlined/link.svg");
        var hotkeysGroup = new SettingsGroupViewModel("Shortcuts");
        hotkeysGroup.Items.Add(new ActionSettingViewModel("Global Quick Actions", "Press Alt+Shift+Q to open the quick action menu.", "Change Bind"));
        hotkeysGroup.Items.Add(new ActionSettingViewModel("New Note", "Press Ctrl+N to create a new note.", "Change Bind"));
        hotkeys.Groups.Add(hotkeysGroup);

        Categories.Add(account);
        Categories.Add(general);
        Categories.Add(editor);
        Categories.Add(aiTools);
        Categories.Add(appearance);
        Categories.Add(hotkeys);
        
        SelectedCategory = general;
    }
}
