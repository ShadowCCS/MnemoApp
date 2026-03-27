namespace Mnemo.Core.Models;

/// <summary>Localization keys (namespace Chat) reported via <see cref="System.IProgress{T}"/> during streaming prompts.</summary>
public static class ChatPipelineStatusKeys
{
    public const string Routing = "PipelineStatusRouting";
    public const string Processing = "PipelineStatusProcessing";
    public const string LoadingSkills = "PipelineStatusLoadingSkills";
    public const string Classifying = "PipelineStatusClassifying";
    public const string ReadingSkill = "PipelineStatusReadingSkill";
    public const string PreparingModel = "PipelineStatusPreparingModel";
    /// <summary>High-level label for the routing + skills phase in the process thread UI.</summary>
    public const string RoutingCombined = "PipelineStatusRoutingCombined";
    /// <summary>Model is generating the visible reply (first pass).</summary>
    public const string Generating = "PipelineStatusGenerating";
    /// <summary>Another generation pass after tool results were applied.</summary>
    public const string ContinuingAfterTool = "PipelineStatusContinuingAfterTool";
    /// <summary>Prefix for <see cref="RunningTool"/> composite keys (see <see cref="RunningTool"/>).</summary>
    public const string RunningToolPrefix = "RT:";

    /// <summary>Composite key: parse with <see cref="TryParseRunningTool"/>.</summary>
    public static string RunningTool(string toolName) => $"{RunningToolPrefix}{toolName}";

    /// <summary>Returns the tool name when <paramref name="key"/> was produced by <see cref="RunningTool"/>.</summary>
    public static bool TryParseRunningTool(string key, out string toolName)
    {
        toolName = string.Empty;
        if (string.IsNullOrEmpty(key) || !key.StartsWith(RunningToolPrefix, StringComparison.Ordinal))
            return false;
        toolName = key[RunningToolPrefix.Length..];
        return !string.IsNullOrEmpty(toolName);
    }
}
