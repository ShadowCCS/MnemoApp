using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using MnemoApp.Core.Shell;
using MnemoApp.Core.Navigation;
using MnemoApp.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using MnemoApp.Core.Services;
using System;

namespace MnemoApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplicationHost.Initialize();
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            // Get services from the host
            var mainWindow = ApplicationHost.Services.GetRequiredService<MainWindow>();
            var mainWindowViewModel = ApplicationHost.Services.GetRequiredService<MainWindowViewModel>();

            // Initialize theme without blocking UI startup
            _ = InitializeThemeSystemAsync();
            
            mainWindow.DataContext = mainWindowViewModel;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry foundd
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static async Task InitializeThemeSystemAsync()
    {
        try
        {
            var themeService = ApplicationHost.Services.GetRequiredService<IThemeService>();
            if (themeService != null)
            {
                // Load theme from settings on startup
                await themeService.LoadThemeFromSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash the app
            System.Diagnostics.Debug.WriteLine($"Theme initialization failed: {ex.Message}");
        }
    }
}