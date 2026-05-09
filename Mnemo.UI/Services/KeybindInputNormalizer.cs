using Avalonia.Input;
using Mnemo.Core.Models.Keybinds;

namespace Mnemo.UI.Services;

public static class KeybindInputNormalizer
{
    public static KeybindPhysicalInput FromKeyEvent(KeyEventArgs e)
    {
        var m = KeybindModifierMask.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            m |= KeybindModifierMask.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            m |= KeybindModifierMask.Alt;

        var mac = OperatingSystem.IsMacOS();
        if (mac && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            m |= KeybindModifierMask.Primary;
        else if (!mac && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            m |= KeybindModifierMask.Primary;
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            m |= KeybindModifierMask.Ctrl;

        return new KeybindPhysicalInput(m, e.Key.ToString());
    }
}
