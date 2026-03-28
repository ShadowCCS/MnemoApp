using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaUI.DiagnosticsSupport;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.AI;
using Mnemo.UI.Modules.Onboarding.Views;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Views;

namespace Mnemo.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public IServiceProvider? Services { get; private set; }

    /// <summary>
    /// Direct reference so we can dispose the server manager on ProcessExit (fallback when Exit doesn't fire).
    /// </summary>
    private static IDisposable? _serverManagerForShutdown;

    public override void OnFrameworkInitializationCompleted()
    {
        Services = Bootstrapper.Build();
        var navService = Services.GetRequiredService<INavigationService>();
        var themeService = Services.GetRequiredService<IThemeService>();

        // Hold reference for shutdown so llama-server processes are always killed (even if Exit doesn't fire)
        _serverManagerForShutdown = Services.GetService(typeof(IAIServerManager)) as IDisposable;

        // Fallback: ensure server processes are stopped when the process exits (e.g. task manager, crash)
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Apply saved theme on startup
        _ = themeService.GetCurrentThemeAsync().ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                _ = themeService.ApplyThemeAsync(t.Result);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
            
            // Cleanup on application exit: dispose AI/servers in order, then the container
            desktop.Exit += (_, _) =>
            {
                try
                {
                    // Stop server processes and release resources in dependency order
                    _serverManagerForShutdown?.Dispose();
                    _serverManagerForShutdown = null;
                    (Services?.GetService(typeof(ITextGenerationService)) as IDisposable)?.Dispose();
                    (Services?.GetService(typeof(IResourceGovernor)) as IDisposable)?.Dispose();
                    (Services?.GetService(typeof(IEmbeddingService)) as IDisposable)?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors during shutdown
                }

                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

                if (Services is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            };
        }

        navService.NavigateTo("overview");

        _ = ShowOnboardingIfNeededAsync();
        _ = RunHardwareInstallMismatchCheckAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private async Task RunHardwareInstallMismatchCheckAsync()
    {
        await Task.Delay(1500).ConfigureAwait(false);
        if (Services == null)
        {
            return;
        }

        var settings = Services.GetRequiredService<ISettingsService>();
        var onboardingDone = await settings.GetAsync("Onboarding.Completed", false).ConfigureAwait(false);
        var registry = Services.GetRequiredService<IAIModelRegistry>();
        await registry.RefreshAsync().ConfigureAwait(false);

        var models = await registry.GetAvailableModelsAsync().ConfigureAwait(false);
        var hasMid = models.Any(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Mid);
        var hasHigh = models.Any(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.High);

        var detector = Services.GetRequiredService<HardwareDetector>();
        var hardware = detector.Detect();
        var tierEval = Services.GetRequiredService<IHardwareTierEvaluator>();
        var tier = tierEval.EvaluateTier(hardware);

        var mismatch =
            (tier == HardwarePerformanceTier.Low && (hasMid || hasHigh)) ||
            (tier == HardwarePerformanceTier.Mid && hasHigh);

        if (!mismatch)
        {
            return;
        }

        var logger = Services.GetRequiredService<ILoggerService>();
        logger.Warning(
            "Hardware",
            $"Detected hardware tier ({tier}) is below installed text model tiers. Mid installed: {hasMid}, High installed: {hasHigh}. VRAM reported: {hardware.TotalVramBytes / 1024 / 1024} MB.");

        if (!onboardingDone)
        {
            return;
        }

        var loc = Services.GetRequiredService<ILocalizationService>();
        var title = loc.T("ModelTierMismatchTitle", "Hardware");
        var message = loc.T("ModelTierMismatchMessage", "Hardware");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Services == null)
            {
                return;
            }

            var overlay = Services.GetRequiredService<IOverlayService>();
            await overlay.CreateDialogAsync(title, message, loc.T("OK", "Common"), "").ConfigureAwait(false);
        });
    }

    private async Task ShowOnboardingIfNeededAsync()
    {
        if (Services == null) return;
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var completed = await settingsService.GetAsync("Onboarding.Completed", false).ConfigureAwait(false);
        if (completed) return;

        var vm = Services.GetRequiredService<Mnemo.UI.Modules.Onboarding.ViewModels.OnboardingWizardViewModel>();
        await vm.LoadUserNameAsync().ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Services == null) return;
            var overlayService = Services.GetRequiredService<IOverlayService>();
            var view = new OnboardingWizardView { DataContext = vm };
            var options = new OverlayOptions
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ShowBackdrop = true,
                CloseOnOutsideClick = false,
                CloseOnEscape = false
            };
            var id = overlayService.CreateOverlay(view, options, "OnboardingWizard");
            vm.SetOverlayId(id);
        });
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        ShutdownServerManager();
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            ShutdownServerManager();
        }
        catch
        {
            // Ignore disposal errors during crash
        }
    }

    private static void ShutdownServerManager()
    {
        try
        {
            _serverManagerForShutdown?.Dispose();
            _serverManagerForShutdown = null;
        }
        catch
        {
            // Ignore disposal errors during process exit or crash
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
