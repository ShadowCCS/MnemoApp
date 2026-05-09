namespace Mnemo.Core.Models.Keybinds;

public readonly struct KeybindBubbleResult
{
    public KeybindBubbleResult(bool handled, bool completedAction, string? actionId, bool swallowedPrefixOnly)
    {
        Handled = handled;
        CompletedAction = completedAction;
        ActionId = actionId;
        SwallowedPrefixOnly = swallowedPrefixOnly;
    }

    public bool Handled { get; }
    public bool CompletedAction { get; }
    public string? ActionId { get; }
    public bool SwallowedPrefixOnly { get; }

    public static KeybindBubbleResult NoMatch() => new(false, false, null, false);
}
