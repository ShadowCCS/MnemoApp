using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components;

public partial class TopbarViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IOverlayService _overlayService;
    private readonly IStatisticsManager _statistics;
    private readonly ILoggerService _logger;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;

    public ObservableCollection<TopbarButtonModel> Buttons { get; } = new();

    [ObservableProperty]
    private bool _isGamificationEnabled;

    [ObservableProperty]
    private string _profilePicturePath = "avares://Mnemo.UI/Assets/ProfilePictures/img2.png";

    /// <summary>Lifetime XP from <see cref="AppStatKinds.LifetimeTotals"/> (<c>total_xp</c>).</summary>
    [ObservableProperty]
    private string _gamificationXpText = string.Empty;

    /// <summary>Current practice streak from flashcard lifetime totals (<c>current_streak_days</c>).</summary>
    [ObservableProperty]
    private string _gamificationStreakText = string.Empty;

    public TopbarViewModel(
        ISettingsService settingsService,
        IOverlayService overlayService,
        IStatisticsManager statistics,
        ILoggerService logger,
        INavigationService navigation,
        ILocalizationService localization)
    {
        _settingsService = settingsService;
        _overlayService = overlayService;
        _statistics = statistics;
        _logger = logger;
        _navigation = navigation;
        _localization = localization;

        // Initial load
        _isGamificationEnabled = _settingsService.GetAsync("App.EnableGamification", true).GetAwaiter().GetResult();
        _profilePicturePath = _settingsService.GetAsync("User.ProfilePicture", "avares://Mnemo.UI/Assets/ProfilePictures/img2.png").GetAwaiter().GetResult();
        ApplyGamificationLocalizedDefaults();
        _ = RefreshGamificationFromAnalyticsAsync();

        _navigation.Navigated += (_, _) => _ = RefreshGamificationFromAnalyticsAsync();
        _localization.LanguageChanged += (_, _) =>
        {
            ApplyGamificationLocalizedDefaults();
            _ = RefreshGamificationFromAnalyticsAsync();
        };

        // Listen for changes
        _settingsService.SettingChanged += (s, key) =>
        {
            if (key == "App.EnableGamification")
            {
                IsGamificationEnabled = _settingsService.GetAsync("App.EnableGamification", true).GetAwaiter().GetResult();
            }
            else if (key == "User.ProfilePicture")
            {
                ProfilePicturePath = _settingsService.GetAsync("User.ProfilePicture", "avares://Mnemo.UI/Assets/ProfilePictures/img2.png").GetAwaiter().GetResult();
            }
        };
    }

    private void ApplyGamificationLocalizedDefaults()
    {
        GamificationXpText = string.Format(_localization.T("GamificationXpFormat", "Topbar"), 0);
        GamificationStreakText = string.Format(_localization.T("GamificationStreakFormat", "Topbar"), 0);
    }

    private async Task RefreshGamificationFromAnalyticsAsync()
    {
        try
        {
            var flashTotals = (await _statistics.GetAsync(
                    StatisticsNamespaces.Flashcards,
                    FlashcardStatKinds.LifetimeTotals,
                    "all").ConfigureAwait(false))
                .Value;
            var streak = ReadInt(flashTotals, "current_streak_days");

            var appTotals = (await _statistics.GetAsync(
                    StatisticsNamespaces.App,
                    AppStatKinds.LifetimeTotals,
                    "all").ConfigureAwait(false))
                .Value;
            var xp = ReadInt(appTotals, "total_xp");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                GamificationXpText = string.Format(_localization.T("GamificationXpFormat", "Topbar"), xp);
                GamificationStreakText = string.Format(_localization.T("GamificationStreakFormat", "Topbar"), streak);
            });
        }
        catch (System.Exception ex)
        {
            _logger?.Error("Topbar", "Loading gamification stats from analytics failed.", ex);
            await Dispatcher.UIThread.InvokeAsync(ApplyGamificationLocalizedDefaults);
        }
    }

    private static long ReadInt(StatisticsRecord? record, string field)
    {
        if (record == null) return 0L;
        return record.Fields.TryGetValue(field, out var v) && v.Type == StatValueType.Integer
            ? v.AsInt()
            : 0L;
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        var result = await _overlayService.CreateDialogAsync(
            "Confirm Exit",
            "Are you sure you want to close Mnemo?",
            "Exit",
            "Cancel"
        );

        if (result == "Exit")
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }

    [RelayCommand]
    private void Minimize()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow!.WindowState = Avalonia.Controls.WindowState.Minimized;
        }
    }

    [RelayCommand]
    private void Maximize()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow!.WindowState = desktop.MainWindow.WindowState == Avalonia.Controls.WindowState.Maximized
                ? Avalonia.Controls.WindowState.Normal
                : Avalonia.Controls.WindowState.Maximized;
        }
    }
}



