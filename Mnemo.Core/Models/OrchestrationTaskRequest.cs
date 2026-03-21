namespace Mnemo.Core.Models;

/// <summary>
/// One unit of work for the universal mini-model orchestration API (single or batched).
/// </summary>
public sealed class OrchestrationTaskRequest
{
    public string TaskType { get; init; } = string.Empty;
    public string UserMessage { get; init; } = string.Empty;
}
