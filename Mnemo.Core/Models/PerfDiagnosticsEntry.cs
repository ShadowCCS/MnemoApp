namespace Mnemo.Core.Models;

public sealed record PerfDiagnosticsEntry(
    DateTimeOffset Timestamp,
    PerfDiagnosticsKind Kind,
    string Category,
    string Name,
    string Value,
    string? Detail = null);
