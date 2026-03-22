using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Background mini model (0.6B) for routing and future task types. Not user-facing.
/// </summary>
public interface IOrchestrationLayer
{
    /// <summary>
    /// Classifies the user message for main-model selection (simple vs reasoning) and skill context selection.
    /// </summary>
    Task<Result<RoutingAndSkillDecision>> RouteAndClassifySkillAsync(string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Runs one or more orchestration tasks using the universal TaskType prompt format.
    /// Use <see cref="OrchestrationExecutionMode.Parallel"/> for independent tasks when latency matters.
    /// </summary>
    Task<Result<IReadOnlyList<OrchestrationTaskResult>>> RunTasksAsync(
        IReadOnlyList<OrchestrationTaskRequest> requests,
        OrchestrationExecutionMode executionMode,
        CancellationToken ct = default);
}
