using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mnemo.Core.Models;

/// <summary>Root record for one JSONL line — structured for downstream ML / fine-tuning pipelines.</summary>
public sealed class ChatDatasetTurnRecord
{
    public int SchemaVersion { get; init; } = 5;
    public string RecordedAtUtc { get; init; } = "";
    public string TurnId { get; init; } = "";
    public string ConversationId { get; init; } = "";

    /// <summary>Zero-based index of this turn within the conversation (0 = first user message after the welcome).</summary>
    public int TurnIndex { get; init; }

    public string Source { get; init; } = "";
    public string AssistantMode { get; init; } = "";
    public string LatestUserMessage { get; init; } = "";
    public string? ConversationContext { get; init; }
    public string ComposedSystemPrompt { get; init; } = "";

    /// <summary>
    /// The final text the assistant produced that was shown to the user.
    /// Populated at commit time from the UI; never empty for successful turns.
    /// This is the single reliable field for "what did the model say" regardless of path (tool or non-tool).
    /// </summary>
    public string? FinalAssistantResponse { get; init; }

    public ChatDatasetManagerSection? Manager { get; init; }
    public ChatDatasetChatSection? Chat { get; init; }
    public ChatDatasetOutcomeSection Outcome { get; init; } = new();
}

/// <summary>
/// Exact routing-step model inputs: Vertex uses <see cref="SystemInstruction"/> + user task block;
/// local manager uses <see cref="LocalManagerFullPrompt"/> as the single generate string.
/// </summary>
public sealed class ChatDatasetRoutingModelInput
{
    /// <summary>Vertex routing only: system instruction sent as <c>systemInstruction</c>. Null for local manager.</summary>
    public string? SystemInstruction { get; init; }

    /// <summary>
    /// User-side task block (skills + message + tool hint). Same as <see cref="ChatDatasetManagerSection.UserBlock"/>.
    /// Sent to Vertex as the user message text; embedded inside the local chat template.
    /// </summary>
    public string UserTaskBlock { get; init; } = "";

    /// <summary>
    /// Local manager only: exact string passed to <c>GenerateAsync</c> (chat template + task block). Null when routing uses Vertex.
    /// </summary>
    public string? LocalManagerFullPrompt { get; init; }
}

public sealed class ChatDatasetManagerSection
{
    public string? ModelId { get; init; }
    public string? ModelDisplayName { get; init; }
    public IReadOnlyList<string>? EnabledSkillIds { get; init; }

    /// <summary>
    /// Canonical snapshot of what was passed <b>into</b> the routing model (system + user task block, or full local prompt).
    /// Prefer this for training over reading separate fields.
    /// </summary>
    public ChatDatasetRoutingModelInput? RoutingModelInput { get; init; }

    /// <summary>
    /// Routing task block: TaskType, available skills, skill descriptions, optional tool hint, and <c>[USER MESSAGE]</c>.
    /// This is the primary <b>routing input</b> (user turn) for both Vertex and the local manager.
    /// </summary>
    public string? UserBlock { get; init; }

    /// <summary>
    /// How routing ran: <c>vertex_teacher</c> (Gemini) or <c>local_manager</c> (on-device task model).
    /// </summary>
    public string? RoutingProvider { get; init; }

    /// <summary>
    /// System instruction for the router (Gemini teacher only). Null when using the local manager with an empty template system slot.
    /// Together with <see cref="UserBlock"/> this matches the API input for teacher routing.
    /// </summary>
    public string? RoutingSystemInstruction { get; init; }

    /// <summary>
    /// Full chat-formatted prompt sent to the local manager model (template + user block). Empty or placeholder when <see cref="RoutingProvider"/> is <c>vertex_teacher</c>.
    /// </summary>
    public string FullPrompt { get; init; } = "";

    /// <summary>Raw JSON string from the router model (routing <b>output</b> before parsing).</summary>
    public string? ResponseRaw { get; init; }

    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>Parsed routing decision (structured routing output for training labels).</summary>
    public ChatDatasetRoutingDecision? ParsedDecision { get; init; }
}

public sealed class ChatDatasetRoutingDecision
{
    public string Complexity { get; init; } = "";
    /// <summary>Ordered skill ids (same contract as router JSON <c>skills</c>).</summary>
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();

    /// <summary>Legacy captures used <c>skill</c> only; deserializes old JSONL for export.</summary>
    [JsonPropertyName("skill")]
    public string? LegacySkill { get; init; }

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

    /// <summary>
    /// Full ordered message list as sent to the model (system + prior turns + current user message).
    /// Present on history-aware and tool-loop paths; null on the legacy flat-prompt path.
    /// This is the complete model input context for training purposes.
    /// </summary>
    public IReadOnlyList<ChatDatasetMessage>? MessageHistory { get; init; }

    /// <summary>Tool schemas that were advertised to the model for this turn. Null when no tools were active.</summary>
    public IReadOnlyList<ChatDatasetToolDefinition>? ActiveTools { get; init; }

    /// <summary>
    /// Each agentic round: what tool calls the model requested and the results returned.
    /// Populated only on turns where the model invoked at least one tool.
    /// </summary>
    public IReadOnlyList<ChatDatasetToolRound>? ToolRounds { get; init; }
}

/// <summary>One message in the conversation as sent to the model.</summary>
public sealed class ChatDatasetMessage
{
    /// <summary>Role: "system", "user", "assistant", or "tool".</summary>
    public string Role { get; init; } = "";
    public string? Content { get; init; }
    /// <summary>Tool call id, present when Role is "tool".</summary>
    public string? ToolCallId { get; init; }
    /// <summary>Tool name, present when Role is "tool".</summary>
    public string? ToolName { get; init; }
}

/// <summary>Tool schema advertised to the model for a turn.</summary>
public sealed class ChatDatasetToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>JSON Schema for the tool parameters, serialized as a string.</summary>
    public string? ParametersJson { get; init; }
}

/// <summary>One round of the agentic tool-call loop.</summary>
public sealed class ChatDatasetToolRound
{
    public int Round { get; init; }
    public IReadOnlyList<ChatDatasetToolCall> ToolCalls { get; init; } = [];
}

/// <summary>A single tool call the model requested, plus the result that was returned to it.</summary>
public sealed class ChatDatasetToolCall
{
    public string ToolCallId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? ArgumentsJson { get; init; }
    public string? ResultContent { get; init; }
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

    /// <summary>Zero-based index of this turn within the conversation.</summary>
    public int TurnIndex { get; init; }

    public string Source { get; init; } = "";
    public string AssistantMode { get; init; } = "";
    public string LatestUserMessage { get; init; } = "";
    public string? ConversationContext { get; init; }
    public string ComposedSystemPrompt { get; init; } = "";

    /// <summary>The final assistant text shown to the user — captured from the UI after streaming completes.</summary>
    public string? FinalAssistantResponse { get; init; }

    public ChatDatasetOutcomeSection Outcome { get; init; } = new();
}
