using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mnemo.Core.Models;

public enum BlockType
{
    Text,
    Heading1,
    Heading2,
    Heading3,
    BulletList,
    NumberedList,
    Checklist,
    Quote,
    Code,
    Divider
}

public class Block
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public BlockType Type { get; set; }

    /// <summary>
    /// Structured inline runs (source of truth for rich text).
    /// When null or empty, <see cref="Content"/> is used as a plain-text fallback.
    /// </summary>
    public List<InlineRun>? InlineRuns { get; set; }

    /// <summary>
    /// Flattened plain text derived from <see cref="InlineRuns"/>.
    /// Setting this replaces runs with a single unstyled run.
    /// </summary>
    [JsonInclude]
    public string Content
    {
        get => InlineRuns is { Count: > 0 }
            ? Formatting.InlineRunFormatApplier.Flatten(InlineRuns)
            : _legacyContent ?? string.Empty;
        set
        {
            _legacyContent = value;
            if (InlineRuns == null || InlineRuns.Count == 0)
            {
                InlineRuns = new List<InlineRun> { InlineRun.Plain(value ?? string.Empty) };
            }
        }
    }

    [JsonIgnore]
    private string? _legacyContent;

    public Dictionary<string, object> Meta { get; set; } = new();
    public int Order { get; set; }

    /// <summary>
    /// Ensures InlineRuns is populated. Call after deserialization when InlineRuns might be null
    /// but Content was set from JSON.
    /// </summary>
    public void EnsureInlineRuns()
    {
        if (InlineRuns is not { Count: > 0 })
        {
            InlineRuns = new List<InlineRun>
            {
                InlineRun.Plain(_legacyContent ?? string.Empty)
            };
        }
    }
}
