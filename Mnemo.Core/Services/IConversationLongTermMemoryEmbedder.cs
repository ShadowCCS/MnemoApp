using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Embeds a conversation summary into the vector store (Tier-3 long-term memory)
/// so it can be semantically recalled in future turns or resumed sessions.
/// Called after summarization when the conversation exceeds the Tier-3 threshold.
/// </summary>
public interface IConversationLongTermMemoryEmbedder
{
    /// <summary>
    /// Embeds the snapshot's latest summary into the vector store with a
    /// conversation-scoped <c>scopeId</c> and marks the snapshot as having long-term memory.
    /// Does nothing if no summary is present or the conversation is already embedded.
    /// </summary>
    Task EmbedSummaryAsync(ConversationMemorySnapshot snapshot, CancellationToken ct = default);
}
