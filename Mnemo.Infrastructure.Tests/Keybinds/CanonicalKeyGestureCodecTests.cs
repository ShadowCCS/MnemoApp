using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services.Keybinds;

namespace Mnemo.Infrastructure.Tests.Keybinds;

public sealed class CanonicalKeyGestureCodecTests
{
    [Fact]
    public void RoundTrip_PrimaryShiftK()
    {
        const string s = "Primary+Shift+K";
        var chord = CanonicalKeyGestureCodec.ParseChord(s);
        Assert.Equal(KeybindModifierMask.Primary | KeybindModifierMask.Shift, chord.Modifiers);
        Assert.Equal("K", chord.KeyToken);
        Assert.Equal(s, CanonicalKeyGestureCodec.ToCanonicalString(chord));
    }

    [Fact]
    public void NormalizeChordString_ReordersModifiers()
    {
        var norm = CanonicalKeyGestureCodec.NormalizeChordString("Shift+Ctrl+Alt+Primary+A");
        Assert.Equal("Alt+Ctrl+Primary+Shift+A", norm);
    }

    [Fact]
    public void DuplicateModifier_Throws()
    {
        Assert.Throws<FormatException>(() => CanonicalKeyGestureCodec.ParseChord("Shift+Shift+A"));
    }

    [Fact]
    public void UnknownModifier_Throws()
    {
        Assert.Throws<FormatException>(() => CanonicalKeyGestureCodec.ParseChord("Hyper+A"));
    }

    [Fact]
    public void ChordsMatch_IgnoresKeyLetterCase()
    {
        var def = new LogicalChord(KeybindModifierMask.Primary, "K");
        var input = new KeybindPhysicalInput(KeybindModifierMask.Primary, "k");
        Assert.True(CanonicalKeyGestureCodec.ChordsMatch(def, input));
    }
}
