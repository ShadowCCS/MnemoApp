namespace Mnemo.Core.Models.Keybinds;

public readonly struct KeybindTunnelResult
{
    public KeybindTunnelResult(bool handled, bool completedAction, string? actionId, bool swallowedPrefixOnly)
    {
        Handled = handled;
        CompletedAction = completedAction;
        ActionId = actionId;
        SwallowedPrefixOnly = swallowedPrefixOnly;
    }

    /// <summary>Whether the tunnel consumed the key (match or swallowed prefix).</summary>
    public bool Handled { get; }
    /// <summary>True when an action completed and router should execute <see cref="ActionId"/>.</summary>
    public bool CompletedAction { get; }
    public string? ActionId { get; }
    /// <summary>Sequence advanced but did not complete.</summary>
    public bool SwallowedPrefixOnly { get; }

    public static KeybindTunnelResult NoMatch() => new(false, false, null, false);
}
