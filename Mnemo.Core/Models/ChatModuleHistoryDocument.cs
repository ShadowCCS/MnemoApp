using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>Root document for persisted Atlas (chat module) sessions.</summary>
public sealed class ChatModuleHistoryDocument
{
    public int Version { get; set; } = 1;

    public List<ChatModulePersistedConversation> Conversations { get; set; } = new();
}

/// <summary>One chat thread in the module sidebar.</summary>
public sealed class ChatModulePersistedConversation
{
    public string Id { get; set; } = string.Empty;

    public DateTime LastActivityUtc { get; set; }

    public string AssistantMode { get; set; } = "General";

    /// <summary>Optional user-defined title for the sidebar; when null, title is derived from the first user message.</summary>
    public string? CustomTitle { get; set; }

    public List<ChatModulePersistedMessage> Messages { get; set; } = new();
}

/// <summary>Serializable chat bubble (no transient UI/streaming state).</summary>
public sealed class ChatModulePersistedMessage
{
    public string Content { get; set; } = string.Empty;

    public bool IsUser { get; set; }

    public DateTime TimestampUtc { get; set; }

    public List<string>? Suggestions { get; set; }

    public List<string>? Sources { get; set; }

    public List<ChatModulePersistedAttachment>? Attachments { get; set; }
}

public sealed class ChatModulePersistedAttachment
{
    public string Path { get; set; } = string.Empty;

    public ChatAttachmentKind Kind { get; set; }

    public string? DisplayName { get; set; }
}
