using System.Collections.Generic;

namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// Describes a typed field on a registered <see cref="StatisticsSchema"/>. Used by the manager
/// to validate writes from both internal modules and the extension tool API.
/// </summary>
public sealed class StatisticsFieldDefinition
{
    public string Name { get; init; } = string.Empty;
    public StatValueType Type { get; init; }

    /// <summary>When true the field must be present on create.</summary>
    public bool Required { get; init; }

    /// <summary>Optional default value applied on create when the field is missing.</summary>
    public StatValue? DefaultValue { get; init; }

    /// <summary>Inclusive lower bound for numeric fields. Ignored for non-numeric types.</summary>
    public StatValue? MinValue { get; init; }

    /// <summary>Inclusive upper bound for numeric fields. Ignored for non-numeric types.</summary>
    public StatValue? MaxValue { get; init; }

    /// <summary>Optional allow-list for string fields. Ignored when null/empty.</summary>
    public IReadOnlyList<string>? AllowedStringValues { get; init; }
}
