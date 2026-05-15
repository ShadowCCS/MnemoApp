using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Lightweight in-memory performance ring buffer for developer diagnostics.
/// </summary>
public interface IPerfDiagnostics
{
    const string EnabledSettingKey = "App.PerformanceDiagnostics";

    bool IsEnabled { get; }

    void RecordTiming(string category, string operation, double milliseconds, string? detail = null);

    void RecordMetric(string category, string name, double value, string? unit = null, string? detail = null);

    void CaptureMemorySnapshot(string label, string? detail = null);

    IReadOnlyList<PerfDiagnosticsEntry> GetRecentEntries(int maxCount = 250);

    string FormatReport(int maxEntries = 250);

    void Clear();
}
