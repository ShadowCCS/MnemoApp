using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Embeds a conversation's rolling summary into the vector store (Tier-3 long-term memory)
/// so it can be semantically recalled in future turns or resumed sessions.
/// Uses <see cref="IEmbeddingService"/> directly to avoid re-chunking overhead
/// (summaries are already compact).
/// </summary>
public sealed class ConversationLongTermMemoryEmbedder : IConversationLongTermMemoryEmbedder
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IConversationMemoryStore _memoryStore;
    private readonly ILoggerService _logger;

    public ConversationLongTermMemoryEmbedder(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IConversationMemoryStore memoryStore,
        ILoggerService logger)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    public async Task EmbedSummaryAsync(ConversationMemorySnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot?.LatestSummary == null)
            return;

        // Build a rich text representation of the summary for embedding
        var summaryText = BuildEmbeddableText(snapshot);
        if (string.IsNullOrWhiteSpace(summaryText))
            return;

        var embeddingResult = await _embeddingService
            .GetEmbeddingAsync(summaryText, ct)
            .ConfigureAwait(false);

        if (!embeddingResult.IsSuccess || embeddingResult.Value == null)
        {
            _logger.Warning("Memory",
                $"Tier3 embed: embedding failed conv={snapshot.ConversationId}: {embeddingResult.ErrorMessage}");
            return;
        }

        var scopeId = ConversationMemoryInjector.ConversationMemoryScopeId(snapshot.ConversationId);
        var chunk = new KnowledgeChunk
        {
            Content = summaryText,
            SourceId = Guid.NewGuid().ToString(),
            ScopeId = scopeId,
            Embedding = embeddingResult.Value,
            Metadata = new Dictionary<string, object>
            {
                { "type", "conversation_memory" },
                { "conversation_id", snapshot.ConversationId },
                { "turn_range", $"1-{snapshot.LatestSummary.CoveredThroughTurn}" },
                { "active_skill", snapshot.LatestSummary.ActiveSkill },
                { "embedded_utc", DateTime.UtcNow.ToString("O") }
            }
        };

        await _vectorStore.SaveChunksAsync([chunk], ct).ConfigureAwait(false);

        _memoryStore.MarkHasLongTermMemory(snapshot.ConversationId);

        _logger.Info("Memory",
            $"Tier3 embed: ok conv={snapshot.ConversationId} throughTurn={snapshot.LatestSummary.CoveredThroughTurn} scope={scopeId}");
    }

    private static string BuildEmbeddableText(ConversationMemorySnapshot snapshot)
    {
        var summary = snapshot.LatestSummary!;
        var sb = new StringBuilder();
        sb.AppendLine(summary.Summary);

        if (summary.ActiveEntities.Count > 0)
            sb.AppendLine(string.Join(", ", summary.ActiveEntities.Select(kvp => $"{kvp.Key}={kvp.Value}")));

        if (summary.KeyFacts.Count > 0)
            sb.AppendLine(string.Join("; ", summary.KeyFacts));

        return sb.ToString().Trim();
    }
}
