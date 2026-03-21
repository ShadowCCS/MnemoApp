namespace Mnemo.Core.Models;

/// <summary>
/// Folder names under <c>%LocalAppData%\mnemo\models\text\</c> and matching <see cref="AIModelManifest.Role"/>.
/// </summary>
public static class AIModelRoles
{
    /// <summary>0.6B mini model: routing and future background tasks (always-on).</summary>
    public const string Manager = "manager";

    /// <summary>Low-tier main model (e.g. 0.8B Qwen): simple queries and low-end reasoning.</summary>
    public const string Low = "low";

    /// <summary>Mid-tier main model (e.g. 2B Ministral): mid hardware reasoning.</summary>
    public const string Mid = "mid";

    /// <summary>High-tier main model (e.g. 3B Ministral): high hardware reasoning.</summary>
    public const string High = "high";
}
