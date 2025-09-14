using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;

namespace MnemoApp.Core.MnemoAPI
{
    public class SystemApi
    {
        // Package helpers are exposed via MnemoAPI.storage for advanced ops.
        // Keep this class focused on window/system-level actions.
        private IClassicDesktopStyleApplicationLifetime? GetDesktopLifetime()
        {
            return Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        }

        public void minimize()
        {
            var desktop = GetDesktopLifetime();
            var window = desktop?.MainWindow;
            if (window != null)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        public void maximize()
        {
            var desktop = GetDesktopLifetime();
            var window = desktop?.MainWindow;
            if (window != null)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
        }

        public async void exit()
        {
            var desktop = GetDesktopLifetime();
            if (desktop != null)
            {
                // Clean shutdown of all services before terminating
                try
                {
                    await ApplicationHost.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during application shutdown: {ex.Message}");
                }
                finally
                {
                    desktop.Shutdown();
                }
            }
        }
    }
}


