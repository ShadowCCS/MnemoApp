using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Tools.Statistics;

/// <summary>
/// Parameters describing a single record write. <c>Fields</c> are accepted as a free-form JSON
/// dictionary; the tool service coerces values into typed <see cref="Mnemo.Core.Models.Statistics.StatValue"/>
/// instances using the registered schema (or a relaxed inference when no schema exists).
/// </summary>
public sealed class StatsWriteRecordParameters
{
    [JsonPropertyName("namespace")] public string Namespace { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("source_module")] public string? SourceModule { get; set; }
    [JsonPropertyName("fields")] public Dictionary<string, object?>? Fields { get; set; }
    [JsonPropertyName("metadata_json")] public string? MetadataJson { get; set; }
    [JsonPropertyName("merge_mode")] public string? MergeMode { get; set; } // "merge" | "replace"
    [JsonPropertyName("expected_version")] public long? ExpectedVersion { get; set; }
}

public sealed class StatsRecordIdentityParameters
{
    [JsonPropertyName("namespace")] public string Namespace { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
}

public sealed class StatsReadFieldsParameters
{
    [JsonPropertyName("namespace")] public string Namespace { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("fields")] public List<string>? Fields { get; set; }
}

public sealed class StatsIncrementFieldParameters
{
    [JsonPropertyName("namespace")] public string Namespace { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("field")] public string Field { get; set; } = string.Empty;
    [JsonPropertyName("delta")] public long Delta { get; set; } = 1;
    [JsonPropertyName("source_module")] public string? SourceModule { get; set; }
}

public sealed class StatsQueryParameters
{
    [JsonPropertyName("namespace")] public string Namespace { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("key_prefix")] public string? KeyPrefix { get; set; }
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    [JsonPropertyName("updated_after")] public string? UpdatedAfter { get; set; }
    [JsonPropertyName("updated_before")] public string? UpdatedBefore { get; set; }
}

public sealed class StatsListSchemasParameters
{
    [JsonPropertyName("namespace")] public string? Namespace { get; set; }
}
