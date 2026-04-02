using System;

namespace Mnemo.Core.Services;

/// <summary>
/// Per-async-call context for tool dispatch (e.g. conversation key for inject_skill).
/// </summary>
public interface IToolDispatchAmbient
{
    string? ConversationRoutingKey { get; }

    IDisposable Enter(string? conversationRoutingKey);
}
