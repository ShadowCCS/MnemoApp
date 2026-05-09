using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services.Keybinds;

namespace Mnemo.Infrastructure.Tests.Keybinds;

public class KeybindGestureDisplayFormatterTests
{
    [Fact]
    public void FormatChord_PrimaryPlusLetter_NonMac_UsesCtrlPlusUpperLetter()
    {
        if (OperatingSystem.IsMacOS())
            return;

        var ch = new LogicalChord(KeybindModifierMask.Primary, "b");
        var s = KeybindGestureDisplayFormatter.FormatChord(ch);
        Assert.Equal("Ctrl+B", s);
    }

    [Fact]
    public void FormatChord_MacOS_PrimaryPlusLetter_UsesCommandSymbol()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var ch = new LogicalChord(KeybindModifierMask.Primary, "b");
        var s = KeybindGestureDisplayFormatter.FormatChord(ch);
        Assert.Equal("⌘B", s);
    }

    [Fact]
    public void FormatChord_NonMac_ModifierOrder_IsCtrlAltShift()
    {
        if (OperatingSystem.IsMacOS())
            return;

        var ch = new LogicalChord(
            KeybindModifierMask.Ctrl | KeybindModifierMask.Alt | KeybindModifierMask.Shift,
            "K");
        var s = KeybindGestureDisplayFormatter.FormatChord(ch);
        Assert.Equal("Ctrl+Alt+Shift+K", s);
    }
}
