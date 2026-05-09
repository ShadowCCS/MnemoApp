using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services.Keybinds;
using Mnemo.Infrastructure.Services.Keybinds;

namespace Mnemo.Infrastructure.Tests.Keybinds;

public sealed class KeyMapServiceTests
{
    private static KeybindActionDefinition GlobalChord(string id, string gesture) =>
        new()
        {
            ActionId = id,
            Namespace = "core",
            Scope = KeybindScope.Global,
            Enabled = true,
            Bindings = new[]
            {
                new KeybindBindingEntry { Kind = KeybindBindingKind.Chord, Chord = CanonicalKeyGestureCodec.ParseChord(gesture) },
            },
        };

    private static KeybindActionDefinition GlobalSeq(string id, params string[] steps) =>
        new()
        {
            ActionId = id,
            Namespace = "core",
            Scope = KeybindScope.Global,
            Enabled = true,
            Bindings = new[]
            {
                new KeybindBindingEntry
                {
                    Kind = KeybindBindingKind.Sequence,
                    SequenceSteps = steps.Select(CanonicalKeyGestureCodec.ParseChord).ToList(),
                },
            },
        };

    [Fact]
    public void GlobalChord_Matches()
    {
        var logger = new TestLogger();
        var repo = new FakeKeybindRepository();
        var sut = new KeyMapService(repo, logger, new[] { GlobalChord("global.search", "Primary+K") });
        var input = new KeybindPhysicalInput(KeybindModifierMask.Primary, "K");
        var r = sut.ProcessGlobalKeyDown(input, DateTime.UtcNow, SequenceSwallowMode.SwallowOnPrefixAdvance);
        Assert.True(r.Handled);
        Assert.True(r.CompletedAction);
        Assert.Equal("global.search", r.ActionId);
    }

    [Fact]
    public void TextCapture_SuppressesGlobalChord()
    {
        var logger = new TestLogger();
        var repo = new FakeKeybindRepository();
        var sut = new KeyMapService(repo, logger, new[] { GlobalChord("global.search", "Primary+K") });
        sut.EnterTextCapture();
        var input = new KeybindPhysicalInput(KeybindModifierMask.Primary, "K");
        var r = sut.ProcessGlobalKeyDown(input, DateTime.UtcNow, SequenceSwallowMode.SwallowOnPrefixAdvance);
        Assert.False(r.Handled);
    }

    [Fact]
    public void SharedSequenceLeader_SwallowsPrefix_WhenNotSuppressed()
    {
        var logger = new TestLogger();
        var repo = new FakeKeybindRepository();
        var sut = new KeyMapService(repo, logger, new[]
        {
            GlobalSeq("a", "G", "K"),
            GlobalSeq("b", "G", "T"),
        });
        var g = new KeybindPhysicalInput(KeybindModifierMask.None, "G");
        var r = sut.ProcessGlobalKeyDown(g, DateTime.UtcNow, SequenceSwallowMode.SwallowOnPrefixAdvance);
        Assert.True(r.Handled);
        Assert.False(r.CompletedAction);
        Assert.True(r.SwallowedPrefixOnly);
    }

    [Fact]
    public void SharedSequenceLeader_NoSwallow_WhenAllCandidatesSuppressedByPolicy()
    {
        var logger = new TestLogger();
        var repo = new FakeKeybindRepository();
        var sut = new KeyMapService(repo, logger, new[]
        {
            GlobalSeq("a", "G", "K"),
            GlobalSeq("b", "G", "T"),
        });
        sut.PushSuppression(
            "test",
            new KeybindSuppressionPolicy { OnlyActionIds = new[] { "a", "b" } });
        var g = new KeybindPhysicalInput(KeybindModifierMask.None, "G");
        var r = sut.ProcessGlobalKeyDown(g, DateTime.UtcNow, SequenceSwallowMode.SwallowOnPrefixAdvance);
        Assert.False(r.Handled);
    }

    [Fact]
    public void TextCapture_SuppressesSharedLeader()
    {
        var logger = new TestLogger();
        var repo = new FakeKeybindRepository();
        var sut = new KeyMapService(repo, logger, new[]
        {
            GlobalSeq("a", "G", "K"),
            GlobalSeq("b", "G", "T"),
        });
        sut.EnterTextCapture();
        var g = new KeybindPhysicalInput(KeybindModifierMask.None, "G");
        var r = sut.ProcessGlobalKeyDown(g, DateTime.UtcNow, SequenceSwallowMode.SwallowOnPrefixAdvance);
        Assert.False(r.Handled);
    }
}
