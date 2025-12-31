using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Models;

public interface IAITaskStep
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    AITaskStatus Status { get; }
    double Progress { get; }
    string? ErrorMessage { get; }

    Task<Result> ExecuteAsync(CancellationToken ct);
}