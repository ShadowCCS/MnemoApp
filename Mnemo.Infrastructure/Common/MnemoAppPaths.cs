using System;
using System.IO;

namespace Mnemo.Infrastructure.Common;

public static class MnemoAppPaths
{
    private const string ProductFolderName = "Mnemo";

    // Local databases should live in OS-specific per-user directories.
    // Windows: %LOCALAPPDATA%\Mnemo\
    // Linux/macOS: resolved via .NET's LocalApplicationData implementation.
    public static string GetLocalUserDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Path.Combine(localAppData, ProductFolderName);
    }

    public static string GetLocalUserDataFile(string fileName)
        => Path.Combine(GetLocalUserDataRoot(), fileName);
}

