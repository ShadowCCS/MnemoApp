using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services.Keybinds;

namespace Mnemo.Infrastructure.Services.Keybinds;

internal static class KeybindConflictAnalyzer
{
    public static IReadOnlyList<KeybindConflict> Analyze(IReadOnlyList<KeybindActionDefinition> armed)
    {
        var conflicts = new List<KeybindConflict>();
        var globalChords = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var localChords = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        var globalSeqs = new List<(string Id, LogicalChord[] Steps)>();
        var localSeqs = new List<(string Id, string Ns, LogicalChord[] Steps)>();

        foreach (var def in armed)
        {
            if (!def.Enabled) continue;
            foreach (var b in def.Bindings)
            {
                if (b.Kind == KeybindBindingKind.Chord && b.Chord is { } c)
                {
                    var key = CanonicalKeyGestureCodec.ToCanonicalString(c);
                    if (def.Scope == KeybindScope.Global)
                        AddChord(globalChords, key, def.ActionId);
                    else
                        AddLocalChord(localChords, def.Namespace, key, def.ActionId);
                }
                else if (b.Kind == KeybindBindingKind.Sequence && b.SequenceSteps is { Count: > 0 } steps)
                {
                    var arr = steps.ToArray();
                    if (def.Scope == KeybindScope.Global)
                        globalSeqs.Add((def.ActionId, arr));
                    else
                        localSeqs.Add((def.ActionId, def.Namespace, arr));
                }
            }
        }

        foreach (var (canon, ids) in globalChords)
        {
            if (ids.Count > 1)
            {
                conflicts.Add(new KeybindConflict
                {
                    Severity = KeybindConflictSeverity.Error,
                    Message = $"Global chord '{canon}' is bound to multiple actions.",
                    ActionIdA = ids[0],
                    ActionIdB = ids[1]
                });
            }
        }

        foreach (var nsDict in localChords.Values)
        {
            foreach (var (canon, ids) in nsDict)
            {
                if (ids.Count > 1)
                    conflicts.Add(new KeybindConflict
                    {
                        Severity = KeybindConflictSeverity.Error,
                        Message = $"Local chord '{canon}' conflicts in the same namespace.",
                        ActionIdA = ids[0],
                        ActionIdB = ids[1]
                    });
            }
        }

        foreach (var (canon, ids) in globalChords)
        {
            foreach (var (seqId, steps) in globalSeqs)
            {
                if (steps.Length == 0) continue;
                var first = CanonicalKeyGestureCodec.ToCanonicalString(steps[0]);
                if (string.Equals(canon, first, StringComparison.Ordinal))
                {
                    conflicts.Add(new KeybindConflict
                    {
                        Severity = KeybindConflictSeverity.Error,
                        Message = $"Global chord '{canon}' matches the first step of sequence on '{seqId}' (chord wins; sequence unreachable).",
                        ActionIdA = ids[0],
                        ActionIdB = seqId
                    });
                }
            }
        }

        foreach (var (ns, inner) in localChords)
        {
            foreach (var (seqId, seqNs, steps) in localSeqs)
            {
                if (!string.Equals(ns, seqNs, StringComparison.Ordinal)) continue;
                if (steps.Length == 0) continue;
                var first = CanonicalKeyGestureCodec.ToCanonicalString(steps[0]);
                if (!inner.TryGetValue(first, out var chordIds) || chordIds.Count == 0) continue;
                conflicts.Add(new KeybindConflict
                {
                    Severity = KeybindConflictSeverity.Error,
                    Message = $"Local chord '{first}' matches first step of sequence '{seqId}' in namespace '{ns}'.",
                    ActionIdA = chordIds[0],
                    ActionIdB = seqId
                });
            }
        }

        PairwiseSequences(globalSeqs.Select(s => (s.Id, Steps: s.Steps)).ToList(), conflicts, "global");
        foreach (var grp in localSeqs.GroupBy(x => x.Ns, StringComparer.Ordinal))
        {
            var list = grp.Select(x => (x.Id, x.Steps)).ToList();
            PairwiseSequences(list, conflicts, $"local:{grp.Key}");
        }

        foreach (var (gCanon, gIds) in globalChords)
        {
            foreach (var (_, inner) in localChords)
            {
                if (!inner.TryGetValue(gCanon, out var lIds) || lIds.Count == 0) continue;
                conflicts.Add(new KeybindConflict
                {
                    Severity = KeybindConflictSeverity.Warning,
                    Message =
                        $"Global can match '{gCanon}' before local dispatch; local '{lIds[0]}' may be unreachable unless the global is suppressed or context-gated.",
                    ActionIdA = gIds[0],
                    ActionIdB = lIds[0]
                });
            }
        }

        return conflicts;
    }

    private static void PairwiseSequences(
        List<(string Id, LogicalChord[] Steps)> seqs,
        List<KeybindConflict> conflicts,
        string label)
    {
        for (var i = 0; i < seqs.Count; i++)
        {
            for (var j = i + 1; j < seqs.Count; j++)
            {
                var a = seqs[i];
                var b = seqs[j];
                if (SequenceEqual(a.Steps, b.Steps))
                {
                    conflicts.Add(new KeybindConflict
                    {
                        Severity = KeybindConflictSeverity.Error,
                        Message = $"Identical {label} sequence for '{a.Id}' and '{b.Id}'.",
                        ActionIdA = a.Id,
                        ActionIdB = b.Id
                    });
                    continue;
                }

                if (IsFullPrefix(a.Steps, b.Steps) || IsFullPrefix(b.Steps, a.Steps))
                {
                    conflicts.Add(new KeybindConflict
                    {
                        Severity = KeybindConflictSeverity.Error,
                        Message = $"Sequence '{a.Id}' is a full prefix of '{b.Id}' ({label}); not allowed.",
                        ActionIdA = a.Id,
                        ActionIdB = b.Id
                    });
                }
            }
        }
    }

    private static bool SequenceEqual(LogicalChord[] a, LogicalChord[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }

        return true;
    }

    private static bool IsFullPrefix(LogicalChord[] shorter, LogicalChord[] longer)
    {
        if (shorter.Length >= longer.Length) return false;
        for (var i = 0; i < shorter.Length; i++)
        {
            if (!shorter[i].Equals(longer[i])) return false;
        }

        return true;
    }

    private static void AddChord(Dictionary<string, List<string>> map, string canon, string actionId)
    {
        if (!map.TryGetValue(canon, out var list))
        {
            list = new List<string>();
            map[canon] = list;
        }

        if (!list.Contains(actionId))
            list.Add(actionId);
    }

    private static void AddLocalChord(Dictionary<string, Dictionary<string, List<string>>> map, string ns, string canon, string actionId)
    {
        if (!map.TryGetValue(ns, out var inner))
        {
            inner = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            map[ns] = inner;
        }

        if (!inner.TryGetValue(canon, out var list))
        {
            list = new List<string>();
            inner[canon] = list;
        }

        if (!list.Contains(actionId))
            list.Add(actionId);
    }
}
