using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>Loads and saves chat module conversation history with a fixed retention window.</summary>
public interface IChatModuleHistoryService
{
    /// <summary>Loads history, applies retention pruning, and may rewrite storage if entries were removed.</summary>
    Task<Result<ChatModuleHistoryDocument>> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(ChatModuleHistoryDocument document, CancellationToken cancellationToken = default);
}
