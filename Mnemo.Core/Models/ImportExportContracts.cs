using System.Collections.Generic;

namespace Mnemo.Core.Models;

public sealed class ImportExportCapability
{
    public string ContentType { get; set; } = string.Empty;

    public string FormatId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<string> Extensions { get; set; } = new();

    public bool SupportsImport { get; set; }

    public bool SupportsExport { get; set; }
}

public sealed class ImportExportRequest
{
    public string? ContentType { get; set; }

    public string? FormatId { get; set; }

    public required string FilePath { get; set; }

    public object? Payload { get; set; }

    public Dictionary<string, object?> Options { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}

public sealed class ImportExportPreview
{
    public bool CanImport { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public string FormatId { get; set; } = string.Empty;

    public Dictionary<string, int> DiscoveredCounts { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; set; } = new();
}

public sealed class ImportExportResult
{
    public bool Success { get; set; } = true;

    public string ContentType { get; set; } = string.Empty;

    public string FormatId { get; set; } = string.Empty;

    public Dictionary<string, int> ProcessedCounts { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; set; } = new();

    public string? ErrorMessage { get; set; }
}
