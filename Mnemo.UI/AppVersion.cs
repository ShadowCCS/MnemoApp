using System.Reflection;

namespace Mnemo.UI;

/// <summary>
/// Provides the application version for display (e.g. in onboarding).
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Gets the informational version (e.g. "0.1.0-dev") or fallback "0.0.0".
    /// </summary>
    public static string GetVersion()
    {
        var attr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? "0.0.0";
    }
}
