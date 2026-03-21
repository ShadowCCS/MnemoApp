namespace Mnemo.Core.Models;

/// <summary>
/// How batched orchestration tasks are executed against the mini model.
/// </summary>
public enum OrchestrationExecutionMode
{
    /// <summary>One request after another (predictable load on the local server).</summary>
    Sequential = 0,

    /// <summary>Concurrent HTTP calls; the server may still serialize internally.</summary>
    Parallel = 1
}
