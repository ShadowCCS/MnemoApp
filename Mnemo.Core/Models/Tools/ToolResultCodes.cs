namespace Mnemo.Core.Models.Tools;

/// <summary>Stable machine-readable codes for tool failures and edge cases.</summary>
public static class ToolResultCodes
{
    public const string Success = "success";
    public const string NotFound = "not_found";
    public const string ValidationError = "validation_error";
    public const string InvalidRange = "invalid_range";
    public const string OutOfRange = "out_of_range";
    public const string Conflict = "conflict";
    public const string InternalError = "internal_error";
    public const string FeatureUnavailable = "feature_unavailable";
    public const string ToolUnavailable = "tool_unavailable";
}
