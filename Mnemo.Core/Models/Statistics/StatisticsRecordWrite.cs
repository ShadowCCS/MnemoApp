using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// Write-side payload accepted by <see cref="Mnemo.Core.Services.IStatisticsManager"/>. Authoritative
/// timestamps and versioning are assigned by the manager; callers only supply identity and content.
/// </summary>
public sealed class StatisticsRecordWrite
{
    public string Namespace { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string SourceModule { get; init; } = string.Empty;

    /// <summary>Field bag to merge or replace (see <c>FieldMergeMode</c> in the manager API).</summary>
    public IReadOnlyDictionary<string, StatValue> Fields { get; init; } = new Dictionary<string, StatValue>(StringComparer.Ordinal);

    /// <summary>Optional JSON metadata payload. When null on update the existing value is preserved.</summary>
    public string? MetadataJson { get; init; }
}
