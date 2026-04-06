using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Onboarding.ViewModels;

public partial class AiModelSetupViewModel : ViewModelBase
{
    private readonly IAIModelsSetupService _aiModelsSetupService;
    private readonly IAIModelInstallCoordinator _installCoordinator;
    private readonly ILocalizationService _localizationService;
    private readonly IMainThreadDispatcher _mainThreadDispatcher;

    private IReadOnlyDictionary<string, long> _sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private string? _optionalVisionComponentName;
    private bool _eventsAttached;

    [ObservableProperty] private string _hardwareTierDescription = string.Empty;
    [ObservableProperty] private string _installedSummary = string.Empty;
    [ObservableProperty] private string _requiredMissingSummary = string.Empty;
    [ObservableProperty] private string _transparencyText = string.Empty;
    [ObservableProperty] private bool _showVisionOption;
    [ObservableProperty] private string _visionOptionDescription = string.Empty;
    [ObservableProperty] private bool _includeVisionBundle;
    [ObservableProperty] private string _estimatedDownloadText = string.Empty;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatusMessage = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isDownloadComplete;
    [ObservableProperty] private string? _downloadError;
    [ObservableProperty] private bool _showDownloadButton = true;

    public bool ShowDownloadError => !string.IsNullOrEmpty(DownloadError);

    public AiModelSetupViewModel(
        IAIModelsSetupService aiModelsSetupService,
        IAIModelInstallCoordinator installCoordinator,
        ILocalizationService localizationService,
        IMainThreadDispatcher mainThreadDispatcher)
    {
        _aiModelsSetupService = aiModelsSetupService;
        _installCoordinator = installCoordinator;
        _localizationService = localizationService;
        _mainThreadDispatcher = mainThreadDispatcher;
    }

    public async Task InitializeAsync()
    {
        AttachCoordinatorEvents();
        try
        {
            var status = await _aiModelsSetupService.GetSetupStatusAsync().ConfigureAwait(false);
            _sizes = await _aiModelsSetupService.GetComponentDownloadSizesAsync().ConfigureAwait(false);

            HardwareTierDescription = FormatTier(status.HardwareTier);
            InstalledSummary = string.Join(", ", status.Installed.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(InstalledSummary))
                InstalledSummary = T("NoneDetected");

            RequiredMissingSummary = status.RequiredMissing.Count > 0
                ? string.Join(", ", status.RequiredMissing.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                : T("NoneMissing");

            TransparencyText = T("DownloadTransparency");

            _optionalVisionComponentName = status.OptionalImageMissing.FirstOrDefault();
            ShowVisionOption = _optionalVisionComponentName != null;
            VisionOptionDescription = ShowVisionOption
                ? string.Format(T("VisionOptionFormat"), _optionalVisionComponentName)
                : string.Empty;

            IncludeVisionBundle = false;
            RecomputeEstimate(status);
            UpdateDownloadCompleteState(status);
            RecomputeShowDownloadButton(status);

            if (_installCoordinator.IsRunning)
                IsDownloading = true;
        }
        catch (Exception ex)
        {
            DownloadError = ex.Message;
            OnPropertyChanged(nameof(ShowDownloadError));
        }
    }

    public void Detach()
    {
        _installCoordinator.ProgressChanged -= OnCoordinatorProgress;
        _eventsAttached = false;
    }

    private void AttachCoordinatorEvents()
    {
        if (_eventsAttached)
            return;
        _eventsAttached = true;
        _installCoordinator.ProgressChanged += OnCoordinatorProgress;
    }

    private void OnCoordinatorProgress(AIModelsSetupProgress p)
    {
        _ = _mainThreadDispatcher.InvokeAsync(async () =>
        {
            DownloadProgress = p.Progress;
            if (!string.IsNullOrEmpty(p.Message))
                DownloadStatusMessage = p.Message;
            IsDownloading = true;
            await Task.CompletedTask;
        });
    }

    private string FormatTier(HardwarePerformanceTier tier) =>
        tier switch
        {
            HardwarePerformanceTier.Low => T("HardwareTierLow"),
            HardwarePerformanceTier.Mid => T("HardwareTierMid"),
            HardwarePerformanceTier.High => T("HardwareTierHigh"),
            _ => tier.ToString()
        };

    private void RecomputeEstimate(AIModelsSetupStatus status)
    {
        long sum = 0;
        foreach (var name in status.RequiredMissing)
        {
            if (_sizes.TryGetValue(name, out var n))
                sum += n;
        }

        if (IncludeVisionBundle && _optionalVisionComponentName != null &&
            status.OptionalImageMissing.Contains(_optionalVisionComponentName) &&
            _sizes.TryGetValue(_optionalVisionComponentName, out var v))
            sum += v;

        if (sum <= 0)
            EstimatedDownloadText = T("EstimatedSizeUnknown");
        else
            EstimatedDownloadText = string.Format(T("EstimatedDownloadFormat"), FormatBytes(sum));
    }

    private void UpdateDownloadCompleteState(AIModelsSetupStatus status)
    {
        IsDownloadComplete = status.AllRequiredInstalled;
    }

    private void RecomputeShowDownloadButton(AIModelsSetupStatus status)
    {
        var needRequired = status.RequiredMissing.Count > 0;
        var needVision = ShowVisionOption && IncludeVisionBundle && _optionalVisionComponentName != null &&
                         status.OptionalImageMissing.Contains(_optionalVisionComponentName);
        ShowDownloadButton = needRequired || needVision;
    }

    partial void OnIncludeVisionBundleChanged(bool value)
    {
        _ = _mainThreadDispatcher.InvokeAsync(async () =>
        {
            try
            {
                var status = await _aiModelsSetupService.GetSetupStatusAsync().ConfigureAwait(false);
                RecomputeEstimate(status);
                UpdateDownloadCompleteState(status);
                RecomputeShowDownloadButton(status);
            }
            catch
            {
                // ignore
            }
        });
    }

    [RelayCommand]
    private async Task DownloadModelsAsync()
    {
        if (IsDownloading)
            return;

        DownloadError = null;
        OnPropertyChanged(nameof(ShowDownloadError));
        DownloadStatusMessage = T("Starting");
        IsDownloading = true;
        IsDownloadComplete = false;

        IReadOnlySet<string>? optional = null;
        if (IncludeVisionBundle && !string.IsNullOrEmpty(_optionalVisionComponentName))
            optional = new HashSet<string>(new[] { _optionalVisionComponentName }, StringComparer.OrdinalIgnoreCase);

        try
        {
            var result = await _installCoordinator.StartDownloadAsync(optional).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                DownloadError = null;
                DownloadStatusMessage = result.Value!.Installed.Count > 0
                    ? T("Ready")
                    : T("AllModelsInstalled");
                await InitializeAsync().ConfigureAwait(false);
            }
            else
            {
                DownloadError = result.ErrorMessage ?? T("DownloadFailed");
                OnPropertyChanged(nameof(ShowDownloadError));
            }
        }
        catch (Exception ex)
        {
            DownloadError = ex.Message;
            OnPropertyChanged(nameof(ShowDownloadError));
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private string T(string key) => _localizationService.T(key, "Onboarding");

    private static string FormatBytes(long bytes)
    {
        const double k = 1024;
        if (bytes < k)
            return $"{bytes} B";
        if (bytes < k * k)
            return $"{bytes / k:0.##} KB";
        if (bytes < k * k * k)
            return $"{bytes / (k * k):0.##} MB";
        return $"{bytes / (k * k * k):0.##} GB";
    }
}
