using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services.Keybinds;
using Mnemo.Infrastructure.Services.Keybinds;

namespace Mnemo.Infrastructure.Tests.Keybinds;

public sealed class KeybindConflictAnalyzerTests
{
    private static LogicalChord C(string canonical) => CanonicalKeyGestureCodec.ParseChord(canonical);

    [Fact]
    public void DuplicateGlobalChord_IsError()
    {
        var armed = new List<KeybindActionDefinition>
        {
            Def("a", KeybindScope.Global, "core", Chord("Primary+K")),
            Def("b", KeybindScope.Global, "core", Chord("Primary+K")),
        };
        var conflicts = KeybindConflictAnalyzer.Analyze(armed);
        Assert.Contains(conflicts, c => c.Severity == KeybindConflictSeverity.Error && c.Message.Contains("Global chord", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalChord_FirstStepOfSequence_IsError()
    {
        var armed = new List<KeybindActionDefinition>
        {
            Def("chord", KeybindScope.Global, "core", Chord("Primary+K")),
            Def("seq", KeybindScope.Global, "core", Seq("Primary+K", "G")),
        };
        var conflicts = KeybindConflictAnalyzer.Analyze(armed);
        Assert.Contains(conflicts, c => c.Severity == KeybindConflictSeverity.Error && c.Message.Contains("first step", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FullPrefixSequences_IsError()
    {
        var armed = new List<KeybindActionDefinition>
        {
            Def("short", KeybindScope.Global, "core", Seq("G", "K")),
            Def("long", KeybindScope.Global, "core", Seq("G", "K", "T")),
        };
        var conflicts = KeybindConflictAnalyzer.Analyze(armed);
        Assert.Contains(conflicts, c => c.Severity == KeybindConflictSeverity.Error && c.Message.Contains("full prefix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GlobalAndLocalSameChord_IsWarning()
    {
        var armed = new List<KeybindActionDefinition>
        {
            Def("g", KeybindScope.Global, "core", Chord("Primary+K")),
            Def("l", KeybindScope.Local, "notes", Chord("Primary+K")),
        };
        var conflicts = KeybindConflictAnalyzer.Analyze(armed);
        Assert.Contains(conflicts, c => c.Severity == KeybindConflictSeverity.Warning);
    }

    private static KeybindBindingEntry Chord(string canonical) =>
        new() { Kind = KeybindBindingKind.Chord, Chord = C(canonical) };

    private static KeybindBindingEntry Seq(params string[] steps) =>
        new()
        {
            Kind = KeybindBindingKind.Sequence,
            SequenceSteps = steps.Select(C).ToList(),
        };

    private static KeybindActionDefinition Def(string id, KeybindScope scope, string ns, KeybindBindingEntry binding) =>
        new()
        {
            ActionId = id,
            Namespace = ns,
            Scope = scope,
            Enabled = true,
            Bindings = new[] { binding },
        };
}
