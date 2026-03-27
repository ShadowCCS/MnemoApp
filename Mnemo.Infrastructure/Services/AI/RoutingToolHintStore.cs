using System.Collections.Concurrent;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public sealed class RoutingToolHintStore : IRoutingToolHintStore
{
    private readonly ConcurrentDictionary<string, RoutingToolHint> _byConversation = new(StringComparer.Ordinal);

    public void Record(string conversationKey, string skillId, string toolName, string? detail)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
            return;

        _byConversation[conversationKey.Trim()] = new RoutingToolHint(skillId, toolName, detail);
    }

    public RoutingToolHint? TryGet(string conversationKey)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
            return null;

        return _byConversation.TryGetValue(conversationKey.Trim(), out var h) ? h : null;
    }

    public void Clear(string conversationKey)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
            return;

        _byConversation.TryRemove(conversationKey.Trim(), out _);
    }
}
