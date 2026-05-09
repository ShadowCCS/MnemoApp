namespace Mnemo.Core.Models.Keybinds;

/// <summary>One segment for shortcut strip UI: a key pill or a “then” separator inside a sequence.</summary>
public readonly record struct KeybindDisplayPill(bool IsThenSeparator, string Text);
