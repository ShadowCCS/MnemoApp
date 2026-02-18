using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models;

public class KnowledgeChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    /// <summary>Optional scope (e.g. path id). When set, search can be limited to this scope; when null, chunk is global/legacy.</summary>
    public string? ScopeId { get; set; }
    public float RelevanceScore { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public float[]? Embedding { get; set; }
}