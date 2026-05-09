namespace Mnemo.Core.Models.Keybinds;

[Flags]
public enum KeybindModifierMask : byte
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Ctrl = 4,
    /// <summary>Command-primary (⌘ on macOS, Ctrl on Windows/Linux after UI normalization).</summary>
    Primary = 8
}
