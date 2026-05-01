namespace Mnemo.Core.Models;

/// <summary>Additional visibility rules for sidebar entries beyond localization.</summary>
public enum SidebarItemVisibilityRequirement
{
    None,
    /// <summary>Show only when <c>AI.EnableAssistant</c> is true.</summary>
    AiAssistantEnabled,
}
