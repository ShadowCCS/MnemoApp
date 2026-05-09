namespace Mnemo.Core.Models.Keybinds;

public enum SequenceSwallowMode
{
    /// <summary>Partial sequence steps set handled / swallow the key.</summary>
    SwallowOnPrefixAdvance,
    /// <summary>Only the final step swallows.</summary>
    SwallowOnlyOnComplete
}
