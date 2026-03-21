namespace Mnemo.Core.Models;

/// <summary>
/// Raw outcome for a single orchestration task (parsing is task-specific).
/// </summary>
public sealed class OrchestrationTaskResult
{
    public string TaskType { get; init; } = string.Empty;
    public string RawContent { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}
