using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Settings.ViewModels;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Onboarding.ViewModels;

public partial class OnboardingWizardViewModel : ViewModelBase
{
    private const string OnboardingCompletedKey = "Onboarding.Completed";
    private const string UserDisplayNameKey = "User.DisplayName";

    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IOverlayService _overlayService;
    private readonly INavigationService _navigationService;
    private string? _overlayId;

    private static readonly IReadOnlyList<OnboardingStepKind> Steps = new[]
    {
        OnboardingStepKind.Welcome,
        OnboardingStepKind.Language,
        OnboardingStepKind.Personalization,
        OnboardingStepKind.AISetup,
    };

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private string _userName = string.Empty;

    public string VersionText { get; } = global::Mnemo.UI.AppVersion.GetVersion();

    public LanguageSettingViewModel LanguageSettingViewModel { get; }
    public ThemeSettingViewModel ThemeSettingViewModel { get; }
    public AppIconSettingViewModel AppIconSettingViewModel { get; }
    public ProfilePictureSettingViewModel ProfilePictureSettingViewModel { get; }
    public AiModelSetupViewModel AiSetup { get; }

    public int StepCount => Steps.Count;
    public OnboardingStepKind CurrentStep => Steps[CurrentStepIndex];
    public bool IsFirstStep => CurrentStepIndex == 0;
    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;
    public string NextButtonText => IsLastStep ? _localizationService.T("Finish", "Common") : _localizationService.T("Next", "Common");
    public string StepProgressText => $"{CurrentStepIndex + 1} of {StepCount}";
    public bool IsWelcomeStep => CurrentStep == OnboardingStepKind.Welcome;
    public bool IsLanguageStep => CurrentStep == OnboardingStepKind.Language;
    public bool IsPersonalizationStep => CurrentStep == OnboardingStepKind.Personalization;
    public bool IsAISetupStep => CurrentStep == OnboardingStepKind.AISetup;

    public string StepTitle => CurrentStep switch
    {
        OnboardingStepKind.Welcome => _localizationService.T("WelcomeTitle", "Onboarding"),
        OnboardingStepKind.Language => _localizationService.T("LanguageTitle", "Onboarding"),
        OnboardingStepKind.Personalization => _localizationService.T("PersonalizeTitle", "Onboarding"),
        OnboardingStepKind.AISetup => _localizationService.T("AISetupTitle", "Onboarding"),
        _ => string.Empty,
    };

    public string StepDescription => CurrentStep switch
    {
        OnboardingStepKind.Welcome => _localizationService.T("WelcomeDescription", "Onboarding"),
        OnboardingStepKind.Language => _localizationService.T("LanguageDescription", "Onboarding"),
        OnboardingStepKind.Personalization => _localizationService.T("PersonalizeDescription", "Onboarding"),
        OnboardingStepKind.AISetup => _localizationService.T("AISetupDescription", "Onboarding"),
        _ => string.Empty,
    };

    public string VersionLabel => string.Format(_localizationService.T("VersionFormat", "Onboarding"), VersionText);

    public OnboardingWizardViewModel(
        ISettingsService settingsService,
        ILocalizationService localizationService,
        IThemeService themeService,
        IOverlayService overlayService,
        INavigationService navigationService,
        AiModelSetupViewModel aiSetup)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _overlayService = overlayService;
        _navigationService = navigationService;
        AiSetup = aiSetup;

        string T(string key) => _localizationService.T(key, "Onboarding");
        LanguageSettingViewModel = new LanguageSettingViewModel(localizationService, settingsService);
        ThemeSettingViewModel = new ThemeSettingViewModel(themeService, T("Theme"), T("ThemeDescription"));
        AppIconSettingViewModel = new AppIconSettingViewModel(settingsService, T("AppIconLabel"), T("AppIconDescription"));
        ProfilePictureSettingViewModel = new ProfilePictureSettingViewModel(settingsService, T("ProfilePictureLabel"), T("ProfilePictureDescription"));

        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepDescription));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(VersionLabel));
        string T(string key) => _localizationService.T(key, "Onboarding");
        ThemeSettingViewModel.Title = T("Theme");
        ThemeSettingViewModel.Description = T("ThemeDescription");
        AppIconSettingViewModel.Title = T("AppIconLabel");
        AppIconSettingViewModel.Description = T("AppIconDescription");
        ProfilePictureSettingViewModel.Title = T("ProfilePictureLabel");
        ProfilePictureSettingViewModel.Description = T("ProfilePictureDescription");
    }

    public void SetOverlayId(string overlayId)
    {
        _overlayId = overlayId;
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepDescription));
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgressText));
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsLanguageStep));
        OnPropertyChanged(nameof(IsPersonalizationStep));
        OnPropertyChanged(nameof(IsAISetupStep));
        if (CurrentStep == OnboardingStepKind.AISetup)
            _ = AiSetup.InitializeAsync();
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        if (CurrentStepIndex > 0)
        {
            await SavePersonalizationIfNeededAsync();
            CurrentStepIndex--;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsLastStep)
        {
            await CompleteAsync();
            return;
        }
        await SavePersonalizationIfNeededAsync();
        CurrentStepIndex++;
    }

    private async Task SavePersonalizationIfNeededAsync()
    {
        if (CurrentStep == OnboardingStepKind.Personalization && !string.IsNullOrWhiteSpace(UserName))
        {
            await _settingsService.SetAsync(UserDisplayNameKey, UserName.Trim()).ConfigureAwait(false);
        }
    }

    private async Task CompleteAsync()
    {
        await SavePersonalizationIfNeededAsync().ConfigureAwait(false);
        await _settingsService.SetAsync(OnboardingCompletedKey, true).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(_overlayId))
            _overlayService.CloseOverlay(_overlayId);
        _navigationService.NavigateTo("overview");
    }

    public async Task LoadUserNameAsync()
    {
        UserName = await _settingsService.GetAsync(UserDisplayNameKey, string.Empty).ConfigureAwait(false);
    }
}
