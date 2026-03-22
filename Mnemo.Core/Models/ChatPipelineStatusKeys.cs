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
}
