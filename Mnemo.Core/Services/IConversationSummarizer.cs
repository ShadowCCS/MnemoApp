using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Produces a rolling <see cref="ConversationSummary"/> by submitting a
/// <c>convo_summarize</c> task to the manager model (Tier 2 memory).
/// Each call compresses the previous summary + new turns + working-memory facts
/// into a single updated summary, keeping context dense and bounded.
/// </summary>
public interface IConversationSummarizer
{
    /// <summary>
    /// Generates an updated summary covering all turns up to and including the most recent.
    /// </summary>
    /// <param name="snapshot">Current memory snapshot (provides facts and previous summary).</param>
    /// <param name="newTurnsSinceLastSummary">
    /// The turns that have occurred since <see cref="ConversationMemorySnapshot.LastSummarizedTurn"/>,
    /// oldest first. Each turn's tool usage should already be reflected in <paramref name="snapshot"/>.Facts.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<ConversationSummary>> SummarizeAsync(
        ConversationMemorySnapshot snapshot,
        IReadOnlyList<ConversationTurn> newTurnsSinceLastSummary,
        CancellationToken ct = default);
}
