using System;

namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// Filter for <see cref="Mnemo.Core.Services.IStatisticsManager.QueryAsync"/>.
/// </summary>
public sealed class StatisticsQuery
{
    public string Namespace { get; init; } = string.Empty;
    public string? Kind { get; init; }

    /// <summary>Optional prefix match against the record key (e.g. <c>"daily:2026-05"</c>).</summary>
    public string? KeyPrefix { get; init; }

    /// <summary>Inclusive lower bound on <c>UpdatedAt</c>.</summary>
    public DateTimeOffset? UpdatedAfter { get; init; }

    /// <summary>Inclusive upper bound on <c>UpdatedAt</c>.</summary>
    public DateTimeOffset? UpdatedBefore { get; init; }

    /// <summary>Maximum results to return. 0 or negative falls back to a sensible default.</summary>
    public int Limit { get; init; }

    /// <summary>When true, results are ordered by <c>UpdatedAt</c> descending.</summary>
    public bool OrderByUpdatedDescending { get; init; } = true;
}
