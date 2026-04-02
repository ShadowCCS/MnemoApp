using System.Collections.Concurrent;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public sealed class SkillInjectionOverrideStore : ISkillInjectionOverrideStore
{
    private readonly ConcurrentDictionary<string, string> _byConversation = new(StringComparer.Ordinal);

    public void Set(string conversationKey, string? skillId)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
            return;

        var key = conversationKey.Trim();
        if (string.IsNullOrWhiteSpace(skillId))
        {
            _byConversation.TryRemove(key, out _);
            return;
        }

        _byConversation[key] = skillId.Trim();
    }

    public string? TryGet(string conversationKey)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
            return null;

        return _byConversation.TryGetValue(conversationKey.Trim(), out var v) ? v : null;
    }
}
