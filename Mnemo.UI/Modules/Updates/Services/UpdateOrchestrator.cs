using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Layout;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Updates;
using Mnemo.UI.Components.Overlays;
using Mnemo.UI.Modules.Updates.ViewModels;

namespace Mnemo.UI.Modules.Updates.Services;

/// <summary>Startup / manual update checks, gating, overlay, and settings badge.</summary>
public sealed class UpdateOrchestrator : IDisposable
{
    private const int CooldownHours = 6;
    private const int MaxOverlayPrompts = 3;

    private readonly IUpdateService _updateService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly IOverlayService _overlayService;
    private readonly ISidebarService _sidebarService;
    private readonly ILocalizationService _localizationService;
    private readonly IMainThreadDispatcher _mainThreadDispatcher;
    private readonly ILoggerService _logger;

    private AppUpdateInfo? _pendingUpdate;
    private bool _overlayOpen;
    private readonly object _overlayGate = new();
    private bool _started;

    public UpdateOrchestrator(
        IUpdateService updateService,
        ISettingsService settingsService,
        INavigationService navigationService,
        IOverlayService overlayService,
        ISidebarService sidebarService,
        ILocalizationService localizationService,
        IMainThreadDispatcher mainThreadDispatcher,
        ILoggerService logger)
    {
        _updateService = updateService;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _overlayService = overlayService;
        _sidebarService = sidebarService;
        _localizationService = localizationService;
        _mainThreadDispatcher = mainThreadDispatcher;
        _logger = logger;
    }

    /// <summary>Human-readable outcome for the last manual check (Settings button).</summary>
    public string? LastManualCheckMessage { get; private set; }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        _navigationService.Navigated += OnNavigated;
        _ = InitializeAfterStartupAsync();
    }

    public void Dispose()
    {
        _navigationService.Navigated -= OnNavigated;
    }

    private void OnNavigated(object? sender, NavigationChangedEventArgs e) => _ = TryPresentAsync(userForced: false);

    private async Task InitializeAfterStartupAsync()
    {
        try
        {
            await DecrementSnoozeLaunchesAsync().ConfigureAwait(false);
            await RunStartupCheckIfEnabledAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Updates", $"InitializeAfterStartupAsync: {ex.Message}");
        }
    }

    private async Task DecrementSnoozeLaunchesAsync()
    {
        var n = await _settingsService.GetAsync<int?>(UpdateSettingsKeys.SnoozeLaunchesRemaining).ConfigureAwait(false);
        if (n is > 0)
            await _settingsService.SetAsync(UpdateSettingsKeys.SnoozeLaunchesRemaining, n.Value - 1).ConfigureAwait(false);
    }

    private async Task RunStartupCheckIfEnabledAsync()
    {
        var auto = await _settingsService.GetAsync(UpdateSettingsKeys.AutoCheck, true).ConfigureAwait(false);
        if (!auto)
            return;

        await PerformCheckAsync(userInitiated: false).ConfigureAwait(false);
    }

    /// <summary>User clicked "Check for updates" in Settings.</summary>
    public async Task RequestManualCheckAsync()
    {
        LastManualCheckMessage = null;
        await PerformCheckAsync(userInitiated: true).ConfigureAwait(false);
    }

    private async Task PerformCheckAsync(bool userInitiated)
    {
        try
        {
            if (!userInitiated)
            {
                var last = await _settingsService.GetAsync<DateTime?>(UpdateSettingsKeys.LastCheckedUtc).ConfigureAwait(false);
                if (last.HasValue && DateTime.UtcNow - last.Value < TimeSpan.FromHours(CooldownHours))
                    return;
            }

            var result = await _updateService.CheckForUpdatesAsync().ConfigureAwait(false);
            await _settingsService.SetAsync(UpdateSettingsKeys.LastCheckedUtc, DateTime.UtcNow).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                LastManualCheckMessage = userInitiated ? result.ErrorMessage : null;
                _pendingUpdate = null;
                return;
            }

            if (result.Value == null)
            {
                if (userInitiated)
                    LastManualCheckMessage = T("UpdatesUpToDate");
                _pendingUpdate = null;
                SetSettingsUpdateBadge(false);
                return;
            }

            _pendingUpdate = result.Value;
            await TryPresentAsync(userInitiated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Updates", $"PerformCheckAsync: {ex.Message}");
            if (userInitiated)
                LastManualCheckMessage = ex.Message;
        }
    }

    private string T(string key) => _localizationService.T(key, "Settings");

    private async Task TryPresentAsync(bool userForced)
    {
        await _mainThreadDispatcher.InvokeAsync(async () => await TryPresentCoreAsync(userForced).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task TryPresentCoreAsync(bool userForced)
    {
        lock (_overlayGate)
        {
            if (_overlayOpen)
                return;
        }

        if (_pendingUpdate == null)
            return;

        var skipped = await _settingsService.GetAsync<string?>(UpdateSettingsKeys.SkippedVersion).ConfigureAwait(false);
        if (UpdateGatePolicy.IsSkipped(skipped, _pendingUpdate.Version))
        {
            if (userForced)
                LastManualCheckMessage = T("UpdatesSkippedVersionActive");
            return;
        }

        var remind = await _settingsService.GetAsync<DateTime?>(UpdateSettingsKeys.RemindAtUtc).ConfigureAwait(false);
        var launches = await _settingsService.GetAsync<int?>(UpdateSettingsKeys.SnoozeLaunchesRemaining).ConfigureAwait(false);
        if (!userForced && UpdateGatePolicy.IsSnoozeActive(remind, launches))
            return;

        var prompts = await LoadPromptCountsAsync().ConfigureAwait(false);
        prompts.TryGetValue(_pendingUpdate.Version, out var promptCount);
        if (UpdateGatePolicy.IsOverPromptCap(promptCount))
        {
            SetSettingsUpdateBadge(true);
            if (userForced)
                LastManualCheckMessage = T("UpdatesAvailableSeeSettingsBadge");
            return;
        }

        SetSettingsUpdateBadge(false);

        if (!UpdateGatePolicy.IsAllowedRoute(_navigationService.CurrentRoute))
        {
            if (userForced)
                LastManualCheckMessage = T("UpdatesNavigateToOverview");
            return;
        }

        lock (_overlayGate)
            _overlayOpen = true;

        var update = _pendingUpdate;
        var isPortableFlow = !_updateService.SupportsInAppApply;

        void OnClosed()
        {
            lock (_overlayGate)
                _overlayOpen = false;
        }

        var vm = new UpdateAvailableViewModel(
            update,
            _updateService,
            _localizationService,
            _overlayService,
            isPortableFlow,
            onRemindLaterAsync: async () =>
            {
                await _settingsService.SetAsync(UpdateSettingsKeys.RemindAtUtc, DateTime.UtcNow.AddHours(24)).ConfigureAwait(false);
                await _settingsService.SetAsync(UpdateSettingsKeys.SnoozeLaunchesRemaining, 2).ConfigureAwait(false);
                _pendingUpdate = null;
            },
            onRemindTomorrowAsync: async () =>
            {
                await _settingsService.SetAsync(UpdateSettingsKeys.RemindAtUtc, DateTime.UtcNow.AddHours(24)).ConfigureAwait(false);
                await _settingsService.SetAsync<int?>(UpdateSettingsKeys.SnoozeLaunchesRemaining, null).ConfigureAwait(false);
                _pendingUpdate = null;
            },
            onSkipAsync: async () =>
            {
                await _settingsService.SetAsync(UpdateSettingsKeys.SkippedVersion, update.Version).ConfigureAwait(false);
                _pendingUpdate = null;
            });

        vm.OverlayClosed += OnClosed;

        var view = new UpdateAvailableOverlay(vm);
        var options = new OverlayOptions
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = false,
            CloseOnEscape = false
        };
        var overlayId = _overlayService.CreateOverlay(view, options, "UpdateAvailable");
        vm.SetOverlayId(overlayId);

        prompts[_pendingUpdate.Version] = promptCount + 1;
        await _settingsService.SetAsync(UpdateSettingsKeys.PromptCountByVersion, prompts).ConfigureAwait(false);

        if (userForced)
            LastManualCheckMessage = T("UpdatesPromptShown");
    }

    private async Task<Dictionary<string, int>> LoadPromptCountsAsync()
    {
        var raw = await _settingsService.GetAsync<Dictionary<string, int>?>(UpdateSettingsKeys.PromptCountByVersion).ConfigureAwait(false);
        return raw ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private void SetSettingsUpdateBadge(bool visible)
    {
        foreach (var cat in _sidebarService.Categories)
        {
            foreach (var item in cat.Items)
            {
                if (string.Equals(item.Route, "settings", StringComparison.Ordinal))
                {
                    item.ShowUpdateBadge = visible;
                    return;
                }
            }
        }
    }
}
