using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

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

        public void exit()
        {
            var desktop = GetDesktopLifetime();
            if (desktop != null)
            {
                desktop.Shutdown();
            }
        }
    }
}


