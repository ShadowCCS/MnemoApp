using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Holds the most recent tool outcome per conversation for routing (until replaced by a proper memory system).
/// </summary>
public interface IRoutingToolHintStore
{
    void Record(string conversationKey, string skillId, string toolName, string? detail);

    RoutingToolHint? TryGet(string conversationKey);

    void Clear(string conversationKey);

    void ClearAll();
}
