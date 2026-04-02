namespace Mnemo.Core.Models;

/// <summary>
/// TaskType labels for the fine-tuned mini model (must match training prompts).
/// </summary>
public static class OrchestrationTaskTypes
{
    public const string Routing = "routing";
    public const string RoutingAndSkillDetection = "routing_and_skill_detection";

    /// <summary>
    /// Rolling conversation summarization task. Produces a JSON
    /// <c>{ summary, active_entities, key_facts, active_skill }</c> block used as
    /// Tier-2 memory in the chat history composition pipeline.
    /// </summary>
    public const string ConvoSummarize = "convo_summarize";
}
