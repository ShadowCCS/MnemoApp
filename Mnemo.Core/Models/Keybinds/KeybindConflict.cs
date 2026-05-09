namespace Mnemo.Core.Models.Keybinds;

public sealed class KeybindConflict
{
    public required KeybindConflictSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? ActionIdA { get; init; }
    public string? ActionIdB { get; init; }
}
