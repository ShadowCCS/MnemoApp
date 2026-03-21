namespace Mnemo.UI.Services;

/// <summary>Controls reveal pacing for chat streaming (see <c>Chat.StreamingReveal</c>).</summary>
public readonly record struct ChatStreamingDisplayOptions(int TickMs, int CharsPerTick, bool IsInstant)
{
    public const int DefaultUiThrottleMs = 33;

    public static ChatStreamingDisplayOptions Balanced => new(40, 3, false);

    public static ChatStreamingDisplayOptions Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return Balanced;

        switch (stored.Trim().ToLowerInvariant())
        {
            case "instant":
                return new ChatStreamingDisplayOptions(0, 0, true);
            case "smooth":
                return new ChatStreamingDisplayOptions(55, 2, false);
            default:
                return Balanced;
        }
    }
}
