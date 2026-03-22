using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>Root record for one JSONL line — structured for downstream ML / fine-tuning pipelines.</summary>
public sealed class ChatDatasetTurnRecord
{
    public int SchemaVersion { get; init; } = 1;
    public string RecordedAtUtc { get; init; } = "";
    public string TurnId { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string Source { get; init; } = "";
    public string AssistantMode { get; init; } = "";
    public string LatestUserMessage { get; init; } = "";
    public string? ConversationContext { get; init; }
    public string ComposedSystemPrompt { get; init; } = "";
    public ChatDatasetManagerSection? Manager { get; init; }
    public ChatDatasetChatSection? Chat { get; init; }
    public ChatDatasetOutcomeSection Outcome { get; init; } = new();
}

public sealed class ChatDatasetManagerSection
{
    public string? ModelId { get; init; }
    public string? ModelDisplayName { get; init; }
    public IReadOnlyList<string>? EnabledSkillIds { get; init; }
    public string? UserBlock { get; init; }
    public string FullPrompt { get; init; } = "";
    public string? ResponseRaw { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public ChatDatasetRoutingDecision? ParsedDecision { get; init; }
}

public sealed class ChatDatasetRoutingDecision
{
    public string Complexity { get; init; } = "";
    public string Skill { get; init; } = "";
    public string? Confidence { get; init; }
    public string? Reason { get; init; }
}

public sealed class ChatDatasetChatSection
{
    public string? ModelId { get; init; }
    public string? ModelDisplayName { get; init; }
    public string? PromptTemplate { get; init; }
    public string SystemPrompt { get; init; } = "";
    public string UserPromptFull { get; init; } = "";
    public string FormattedPrompt { get; init; } = "";
    public int ImageAttachmentCount { get; init; }
    public string RoutingComplexity { get; init; } = "";
    public string SkillId { get; init; } = "";
    public string? ManagerConfidence { get; init; }
    public string AssistantResponse { get; init; } = "";
}

public sealed class ChatDatasetOutcomeSection
{
    public bool FoundResponse { get; init; }
    public bool Cancelled { get; init; }
    public string? Error { get; init; }
}

public sealed class ChatDatasetCommitRequest
{
    public string TurnId { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string Source { get; init; } = "";
    public string AssistantMode { get; init; } = "";
    public string LatestUserMessage { get; init; } = "";
    public string? ConversationContext { get; init; }
    public string ComposedSystemPrompt { get; init; } = "";
    public ChatDatasetOutcomeSection Outcome { get; init; } = new();
}
