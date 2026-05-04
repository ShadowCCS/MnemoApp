using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Updates.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Updates.ViewModels;

public partial class UpdateAvailableViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private readonly ILocalizationService _localization;
    private readonly IOverlayService _overlayService;
    private readonly bool _isPortableFlow;
    private readonly Func<Task> _onRemindLaterAsync;
    private readonly Func<Task> _onRemindTomorrowAsync;
    private readonly Func<Task> _onSkipAsync;
    private string? _overlayId;

    public UpdateAvailableViewModel(
        AppUpdateInfo update,
        IUpdateService updateService,
        ILocalizationService localization,
        IOverlayService overlayService,
        bool isPortableFlow,
        Func<Task> onRemindLaterAsync,
        Func<Task> onRemindTomorrowAsync,
        Func<Task> onSkipAsync)
    {
        _updateService = updateService;
        _localization = localization;
        _overlayService = overlayService;
        _isPortableFlow = isPortableFlow;
        _onRemindLaterAsync = onRemindLaterAsync;
        _onRemindTomorrowAsync = onRemindTomorrowAsync;
        _onSkipAsync = onSkipAsync;
        Update = update;
        Title = T("UpdateAvailableTitle");
        Subtitle = string.Format(T("UpdateAvailableSubtitleFormat"), update.Version);
        ReleaseNotes = update.ReleaseNotesMarkdown ?? string.Empty;
        HasReleaseNotes = !string.IsNullOrWhiteSpace(ReleaseNotes);
        InstallLabel = T("InstallUpdate");
        RemindLaterLabel = T("RemindLater");
        RemindTomorrowLabel = T("RemindTomorrow");
        SkipLabel = T("SkipThisVersion");
        OpenReleaseLabel = T("OpenReleasePage");
    }

    public AppUpdateInfo Update { get; }

    public string InstallLabel { get; }
    public string RemindLaterLabel { get; }
    public string RemindTomorrowLabel { get; }
    public string SkipLabel { get; }
    public string OpenReleaseLabel { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    public string ReleaseNotes { get; }
    public bool HasReleaseNotes { get; }

    [ObservableProperty]
    private bool _showDownloadProgress;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsPortableFlow => _isPortableFlow;
    public bool CanInstallInApp => !_isPortableFlow && _updateService.SupportsInAppApply;

    public void SetOverlayId(string id) => _overlayId = id;

    /// <summary>Fired right before the overlay is closed (used to clear orchestrator state).</summary>
    public event Action? OverlayClosed;

    private string T(string key) => _localization.T(key, "Settings");

    private void CloseSelf()
    {
        OverlayClosed?.Invoke();
        if (!string.IsNullOrEmpty(_overlayId))
            _overlayService.CloseOverlay(_overlayId);
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!CanInstallInApp)
            return;

        IsBusy = true;
        ShowDownloadProgress = true;
        try
        {
            var progress = new Progress<int>(p => DownloadProgress = p);
            var result = await _updateService.DownloadUpdatesAsync(Update, progress, default).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                Subtitle = result.ErrorMessage ?? T("UpdateDownloadFailed");
                ShowDownloadProgress = false;
                return;
            }

            _updateService.ApplyUpdatesAndRestart();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenReleasePageAsync()
    {
        await UpdateReleaseLauncher.LaunchLatestAsync().ConfigureAwait(true);
        CloseSelf();
    }

    [RelayCommand]
    private async Task RemindLaterAsync()
    {
        await _onRemindLaterAsync().ConfigureAwait(false);
        CloseSelf();
    }

    [RelayCommand]
    private async Task RemindTomorrowAsync()
    {
        await _onRemindTomorrowAsync().ConfigureAwait(false);
        CloseSelf();
    }

    [RelayCommand]
    private async Task SkipThisVersionAsync()
    {
        await _onSkipAsync().ConfigureAwait(false);
        CloseSelf();
    }
}
