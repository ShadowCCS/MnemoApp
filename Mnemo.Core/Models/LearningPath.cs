using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mnemo.Core.Models;

public class LearningPath
{
    [JsonPropertyName("path_id")]
    public string PathId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = "beginner";

    [JsonPropertyName("estimated_time_hours")]
    public double EstimatedTimeHours { get; set; }

    [JsonPropertyName("source_material")]
    public SourceMaterial SourceMaterial { get; set; } = new();

    [JsonPropertyName("units")]
    public List<LearningUnit> Units { get; set; } = new();

    [JsonPropertyName("metadata")]
    public PathMetadata Metadata { get; set; } = new();

    // UI properties
    [JsonIgnore]
    public double Progress => Units.Count == 0 ? 0 : (double)Units.Count(u => u.IsCompleted) / Units.Count * 100;
}

public class SourceMaterial
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "knowledge_base"; // user_upload | knowledge_base

    [JsonPropertyName("document_ids")]
    public List<string> DocumentIds { get; set; } = new();
}

public class LearningUnit
{
    [JsonPropertyName("unit_id")]
    public string UnitId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    [JsonPropertyName("allocated_material")]
    public AllocatedMaterial AllocatedMaterial { get; set; } = new();

    [JsonPropertyName("generation_hints")]
    public GenerationHints GenerationHints { get; set; } = new();

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("is_completed")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("status")]
    public AITaskStatus Status { get; set; } = AITaskStatus.Pending;
}

public class AllocatedMaterial
{
    [JsonPropertyName("chunk_ids")]
    public List<string> ChunkIds { get; set; } = new();

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

public class GenerationHints
{
    [JsonPropertyName("focus")]
    public List<string> Focus { get; set; } = new();

    [JsonPropertyName("avoid")]
    public List<string> Avoid { get; set; } = new();

    [JsonPropertyName("prerequisites")]
    public List<string> Prerequisites { get; set; } = new();
}

public class PathMetadata
{
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}

