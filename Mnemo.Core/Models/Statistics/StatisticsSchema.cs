using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// Optional schema registered for a <c>(Namespace, Kind)</c> pair. When a schema is registered,
/// writes are validated against the field definitions; when no schema exists the manager accepts
/// well-formed writes without strict per-field validation (extension flexibility).
/// </summary>
public sealed class StatisticsSchema
{
    public string Namespace { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<StatisticsFieldDefinition> Fields { get; init; } = Array.Empty<StatisticsFieldDefinition>();

    /// <summary>
    /// When true, fields not present in <see cref="Fields"/> may still be written (typed validation
    /// applies only to declared fields). When false, writes containing undeclared fields are rejected.
    /// </summary>
    public bool AllowAdditionalFields { get; init; } = true;

    /// <summary>Maximum size of <c>MetadataJson</c> in bytes. 0 disables the cap.</summary>
    public int MaxMetadataBytes { get; init; }

    /// <summary>
    /// When non-empty, only the listed source modules / extension ids may write records of this
    /// kind. Empty means any caller may write (subject to other policy).
    /// </summary>
    public IReadOnlyList<string> AllowedSourceModules { get; init; } = Array.Empty<string>();
}
