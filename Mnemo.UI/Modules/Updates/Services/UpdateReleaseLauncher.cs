using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Mnemo.UI.Modules.Updates.Services;

public static class UpdateReleaseLauncher
{
    public const string LatestReleaseUrl = "https://github.com/onemneo/mnemo/releases/latest";

    public static async Task LaunchLatestAsync()
    {
        var uri = new Uri(LatestReleaseUrl);
        var life = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var top = life?.MainWindow;
        if (top == null)
            return;

        await top.Launcher.LaunchUriAsync(uri).ConfigureAwait(true);
    }
}
