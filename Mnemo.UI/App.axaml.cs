using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using Mnemo.UI.Views;

namespace Mnemo.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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

                if (Services is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            };
        }

        navService.NavigateTo("overview");

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            _serverManagerForShutdown?.Dispose();
            _serverManagerForShutdown = null;
        }
        catch
        {
            // Ignore disposal errors during process exit
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
