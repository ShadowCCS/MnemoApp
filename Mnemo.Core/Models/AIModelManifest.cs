using System.Collections.Generic;

namespace Mnemo.Core.Models;

public class AIModelManifest
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AIModelType Type { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public long EstimatedVramUsageBytes { get; set; }
    public string? TokenizerPath { get; set; }
    public string PromptTemplate { get; set; } = "ChatML"; // Default to ChatML
    public Dictionary<string, string> Metadata { get; set; } = new();
}


