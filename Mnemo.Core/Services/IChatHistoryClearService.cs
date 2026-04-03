using System;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>Removes all persisted chat threads, in-memory conversation state, and related vector-store rows.</summary>
public interface IChatHistoryClearService
{
    Task<Result> ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised after a successful clear so the active chat UI can reset without reloading the route.</summary>
    event EventHandler? Cleared;
}
