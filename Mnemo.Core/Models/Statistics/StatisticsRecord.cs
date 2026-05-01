using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// A persisted statistics document. Each record is uniquely identified by the
/// <c>(Namespace, Kind, Key)</c> triple. Fields are typed via <see cref="StatValue"/>;
/// <see cref="MetadataJson"/> is a free-form, validated JSON payload for extension flexibility.
/// </summary>
public sealed class StatisticsRecord
{
    /// <summary>Module/extension owning the data, e.g. <c>"flashcards"</c> or <c>"ext.acme"</c>.</summary>
    public string Namespace { get; init; } = string.Empty;

    /// <summary>Logical record kind within the namespace, e.g. <c>"daily.summary"</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Stable key within (Namespace, Kind), e.g. <c>"2026-05-01"</c> or <c>"deck:abc123"</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the record was first created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp of the most recent successful update.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Monotonically increasing version used for optimistic concurrency control.</summary>
    public long Version { get; init; }

    /// <summary>Module identifier (or extension id) that last wrote the record.</summary>
    public string SourceModule { get; init; } = string.Empty;

    /// <summary>Typed field bag. Keys are case-sensitive ordinals.</summary>
    public IReadOnlyDictionary<string, StatValue> Fields { get; init; } = new Dictionary<string, StatValue>(StringComparer.Ordinal);

    /// <summary>Optional JSON metadata blob, serialized as a string for storage portability.</summary>
    public string? MetadataJson { get; init; }
}
