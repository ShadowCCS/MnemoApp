using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models.Statistics;

namespace Mnemo.Infrastructure.Services.Statistics;

/// <summary>
/// In-memory cache of registered <see cref="StatisticsSchema"/> definitions, keyed by
/// <c>(Namespace, Kind)</c>. Schemas are registered at startup by modules and read on every
/// validating write.
/// </summary>
internal sealed class StatisticsSchemaRegistry
{
    private readonly ConcurrentDictionary<string, StatisticsSchema> _byKey = new(StringComparer.Ordinal);

    public void Register(StatisticsSchema schema)
    {
        if (schema == null) throw new ArgumentNullException(nameof(schema));
        var key = MakeKey(schema.Namespace, schema.Kind);
        _byKey[key] = schema;
    }

    public StatisticsSchema? Get(string ns, string kind)
        => _byKey.TryGetValue(MakeKey(ns, kind), out var s) ? s : null;

    public IReadOnlyList<StatisticsSchema> List(string? ns)
    {
        if (string.IsNullOrEmpty(ns))
            return _byKey.Values.ToList();
        return _byKey.Values
            .Where(s => string.Equals(s.Namespace, ns, StringComparison.Ordinal))
            .ToList();
    }

    private static string MakeKey(string ns, string kind) => ns + "\u001f" + kind;
}
