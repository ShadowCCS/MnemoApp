using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Onboarding;
using Mnemo.UI.Modules.Settings.ViewModels;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Onboarding.ViewModels;

public partial class OnboardingWizardViewModel : ViewModelBase
{
    private const string OnboardingCompletedKey = "Onboarding.Completed";
    private const string UserDisplayNameKey = "User.DisplayName";

    private readonly ISettingsService _settingsService;
    private readonly IOverlayService _overlayService;
    private readonly INavigationService _navigationService;
    private readonly IAIModelsSetupService _aiModelsSetupService;
    private string? _overlayId;
    private CancellationTokenSource? _downloadCts;

    private static readonly IReadOnlyList<OnboardingStepKind> Steps = new[]
    {
        OnboardingStepKind.Welcome,
        OnboardingStepKind.Personalization,
        OnboardingStepKind.AISetup,
    };

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isDownloadComplete;

    [ObservableProperty]
    private string? _downloadError;

    public string VersionText { get; } = global::Mnemo.UI.AppVersion.GetVersion();

    public ThemeSettingViewModel ThemeSettingViewModel { get; }
    public AppIconSettingViewModel AppIconSettingViewModel { get; }
    public ProfilePictureSettingViewModel ProfilePictureSettingViewModel { get; }

    public int StepCount => Steps.Count;
    public OnboardingStepKind CurrentStep => Steps[CurrentStepIndex];
    public bool IsFirstStep => CurrentStepIndex == 0;
    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;
    public string NextButtonText => IsLastStep ? "Finish" : "Next";
    public string StepProgressText => $"{CurrentStepIndex + 1} of {StepCount}";
    public bool ShowDownloadError => !string.IsNullOrEmpty(DownloadError);
    public bool IsWelcomeStep => CurrentStep == OnboardingStepKind.Welcome;
    public bool IsPersonalizationStep => CurrentStep == OnboardingStepKind.Personalization;
    public bool IsAISetupStep => CurrentStep == OnboardingStepKind.AISetup;

    public string StepTitle => CurrentStep switch
    {
        OnboardingStepKind.Welcome => "Welcome to Mnemo",
        OnboardingStepKind.Personalization => "Personalize your experience",
        OnboardingStepKind.AISetup => "Set up Atlas AI",
        _ => string.Empty,
    };

    public string StepDescription => CurrentStep switch
    {
        OnboardingStepKind.Welcome => "This application is under active development. Features may change and data formats are not yet final. Use at your own risk.",
        OnboardingStepKind.Personalization => "Choose your display name, theme, app icon, and profile picture. You can change these later in Settings.",
        OnboardingStepKind.AISetup => "Download the required AI model files (embedding, server, router, and fast) to enable local AI features. Already installed components are skipped; only missing ones are downloaded.",
        _ => string.Empty,
    };

    public OnboardingWizardViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IOverlayService overlayService,
        INavigationService navigationService,
        IAIModelsSetupService aiModelsSetupService)
    {
        _settingsService = settingsService;
        _overlayService = overlayService;
        _navigationService = navigationService;
        _aiModelsSetupService = aiModelsSetupService;

        ThemeSettingViewModel = new ThemeSettingViewModel(themeService, "Theme", "Select the visual style.");
        AppIconSettingViewModel = new AppIconSettingViewModel(settingsService, "App icon", "Icon shown in the taskbar.");
        ProfilePictureSettingViewModel = new ProfilePictureSettingViewModel(settingsService, "Profile picture", "Your avatar in the app.");
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
        OnPropertyChanged(nameof(IsPersonalizationStep));
        OnPropertyChanged(nameof(IsAISetupStep));
        if (CurrentStep == OnboardingStepKind.AISetup)
            _ = RefreshAISetupStatusAsync();
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

    [RelayCommand]
    private async Task DownloadModelsAsync()
    {
        if (IsDownloading || IsDownloadComplete) return;

        IsDownloading = true;
        DownloadError = null;
        DownloadStatusMessage = "Starting...";
        _downloadCts = new CancellationTokenSource();

        var progress = new Progress<AIModelsSetupProgress>(p =>
        {
            DownloadProgress = p.Progress;
            if (!string.IsNullOrEmpty(p.Message))
                DownloadStatusMessage = p.Message;
        });

        try
        {
            var result = await _aiModelsSetupService.DownloadAndExtractMissingAsync(progress, _downloadCts.Token).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                IsDownloadComplete = true;
                DownloadStatusMessage = result.Value!.Installed.Count > 0 ? "Ready" : "All models already installed.";
            }
            else
            {
                DownloadError = result.ErrorMessage ?? "Download failed.";
                OnPropertyChanged(nameof(ShowDownloadError));
            }
        }
        catch (OperationCanceledException)
        {
            DownloadStatusMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            DownloadError = ex.Message;
        }
        finally
        {
            IsDownloading = false;
            OnPropertyChanged(nameof(ShowDownloadError));
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

    /// <summary>
    /// When entering the AI setup step, checks which models are already installed.
    /// If all are installed, shows the finished state so the user is not prompted to download again.
    /// </summary>
    private async Task RefreshAISetupStatusAsync()
    {
        try
        {
            var status = await _aiModelsSetupService.GetSetupStatusAsync().ConfigureAwait(false);
            if (status.AllInstalled)
            {
                IsDownloadComplete = true;
                DownloadStatusMessage = "All models already installed.";
            }
        }
        catch
        {
            // Leave IsDownloadComplete false so user can still try downloading
        }
    }
}
