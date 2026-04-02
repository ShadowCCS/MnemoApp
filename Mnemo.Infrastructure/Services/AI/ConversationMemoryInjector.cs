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
/// Composes the conversation history passed to the main chat model, replacing the flat
/// 11-turn raw-text window with a memory-aware structure:
/// <list type="bullet">
///   <item>Tier-2 rolling summary as a synthetic assistant turn (when available).</item>
///   <item>Tier-3 semantic recall appended to the summary (for long/resumed conversations).</item>
///   <item>The K most recent raw turns verbatim (for turn-level coherence).</item>
/// </list>
/// When no summary exists yet (early in a conversation), falls back to the standard raw
/// sliding window — no regression for short conversations.
/// </summary>
public sealed class ConversationMemoryInjector : IConversationMemoryInjector
{
    /// <summary>Number of recent raw turns to keep verbatim after the summary turn.</summary>
    public const int DefaultRawTailLength = 4;

    /// <summary>
    /// Maximum number of Tier-3 semantic memory chunks to inject per turn.
    /// Kept small to minimize context overhead.
    /// </summary>
    private const int MaxSemanticRecallChunks = 2;

    private const string SummaryTurnPrefix = "[CONVERSATION SUMMARY]\n";

    /// <summary>
    /// Synthetic user line so the first role after <c>system</c> is <c>user</c>. Many chat templates
    /// (llama.cpp / OpenAI-compatible) fail or return empty text when history begins with <c>assistant</c>.
    /// </summary>
    private const string SummaryUserCue =
        "The following assistant message is a compressed summary of earlier turns in this chat. Use it for continuity.";

    private readonly IConversationMemoryStore _memoryStore;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ILoggerService _logger;

    public ConversationMemoryInjector(
        IConversationMemoryStore memoryStore,
        IKnowledgeService knowledgeService,
        ILoggerService logger)
    {
        _memoryStore = memoryStore;
        _knowledgeService = knowledgeService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConversationTurn>> BuildHistoryWithMemoryAsync(
        string conversationId,
        IReadOnlyList<ConversationTurn> allRawTurns,
        string userMessage,
        CancellationToken ct = default)
    {
        var snapshot = _memoryStore.Get(conversationId);

        // No summary yet — fall back to the standard raw window unchanged
        if (snapshot?.LatestSummary == null)
        {
            _logger.Debug("Memory",
                $"Injector: no snapshot/summary for conv={conversationId}; using raw window ({allRawTurns.Count} turns).");
            return allRawTurns;
        }

        var summary = snapshot.LatestSummary;
        if (string.IsNullOrWhiteSpace(summary.Summary))
        {
            _logger.Warning("Memory",
                $"Injector: LatestSummary present but empty prose for conv={conversationId}; falling back to raw window.");
            return allRawTurns;
        }

        // Build the assistant half of the synthetic summary (user cue is separate — see class doc)
        var summaryContent = new StringBuilder();
        summaryContent.Append(SummaryTurnPrefix);
        summaryContent.AppendLine(summary.Summary);

        if (summary.ActiveEntities.Count > 0)
        {
            summaryContent.Append("Active: ");
            summaryContent.AppendLine(string.Join(", ",
                summary.ActiveEntities.Select(kvp => $"{kvp.Key}={kvp.Value}")));
        }

        if (summary.KeyFacts.Count > 0)
        {
            summaryContent.Append("Facts: ");
            summaryContent.AppendLine(string.Join("; ", summary.KeyFacts));
        }

        // Append Tier-3 semantic recall if the conversation has long-term memories
        if (snapshot.HasLongTermMemory && !string.IsNullOrWhiteSpace(userMessage))
        {
            var recalled = await TryRecallSemanticMemoryAsync(conversationId, userMessage, ct)
                .ConfigureAwait(false);
            if (recalled != null)
            {
                summaryContent.AppendLine("[RECALLED CONTEXT]");
                summaryContent.AppendLine(recalled);
                _logger.Debug("Memory", $"Injector: tier3 recall appended for conv={conversationId} (chars={recalled.Length})");
            }
        }

        var assistantSummaryText = summaryContent.ToString().TrimEnd();

        // Take the K most recent raw messages (ConversationTurn list items, not “exchanges”)
        var rawTail = allRawTurns
            .TakeLast(DefaultRawTailLength)
            .ToList();

        // user → assistant → … so templates that require user-first after system still work
        var composed = new List<ConversationTurn>(2 + rawTail.Count)
        {
            new ConversationTurn(ConversationRole.User, SummaryUserCue),
            new ConversationTurn(ConversationRole.Assistant, assistantSummaryText)
        };
        composed.AddRange(rawTail);

        _logger.Info("Memory",
            $"Injector: conv={conversationId} → synthetic U+A summary + {rawTail.Count} raw tail messages " +
            $"(active_skill={summary.ActiveSkill}, entities={summary.ActiveEntities.Count}, tier3={snapshot.HasLongTermMemory})");

        return composed;
    }

    private async Task<string?> TryRecallSemanticMemoryAsync(
        string conversationId,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            var scopeId = ConversationMemoryScopeId(conversationId);
            var searchResult = await _knowledgeService.SearchAsync(
                userMessage,
                limit: MaxSemanticRecallChunks,
                scopeId: scopeId,
                ct: ct).ConfigureAwait(false);

            if (!searchResult.IsSuccess || searchResult.Value == null)
                return null;

            var chunks = searchResult.Value.ToList();
            if (chunks.Count == 0)
                return null;

            return string.Join("\n---\n", chunks.Select(c => c.Content.Trim()));
        }
        catch (Exception ex)
        {
            _logger.Warning("ConversationMemoryInjector",
                $"Tier-3 recall failed for {conversationId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns the vector-store scope ID for a conversation's long-term memories.</summary>
    public static string ConversationMemoryScopeId(string conversationId) =>
        $"conv_mem_{conversationId}";
}
