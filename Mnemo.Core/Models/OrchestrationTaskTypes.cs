namespace Mnemo.Core.Models;

/// <summary>
/// TaskType labels for the fine-tuned mini model (must match training prompts).
/// </summary>
public static class OrchestrationTaskTypes
{
    public const string Routing = "routing";
    public const string RoutingAndSkillDetection = "routing_and_skill_detection";
}
