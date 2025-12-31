using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Models;

public interface IAITask
{
    string Id { get; }
    string DisplayName { get; }
    AITaskStatus Status { get; }
    double TotalProgress { get; }
    IReadOnlyList<IAITaskStep> Steps { get; }
    int CurrentStepIndex { get; }

    Task<Result> RunAsync(CancellationToken ct);
    void Pause();
    void Resume();
    void Cancel();
    Task<Result> RedoStepAsync(int stepIndex, CancellationToken ct);
}