using System.Linq;

namespace Mnemo.Core.Services.Keybinds;

/// <summary>Compact shortcut text for UI badges from merged keymap state (respects user overrides).</summary>
public static class KeybindActionShortcutLabel
{
    public static string ForAction(IKeyMap keyMap, string actionId)
    {
        var def = keyMap.GetAllStaticDefinitionsMerged()
            .FirstOrDefault(d => string.Equals(d.ActionId, actionId, StringComparison.Ordinal));
        if (def is not { Enabled: true } || def.Bindings.Count == 0)
            return "—";
        return string.Join(" · ", def.Bindings.Select(KeybindGestureDisplayFormatter.FormatBindingEntry));
    }
}
