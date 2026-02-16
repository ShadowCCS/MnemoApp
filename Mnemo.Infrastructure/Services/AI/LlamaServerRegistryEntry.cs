namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Persisted entry for crash recovery: allows killing leftover llama-server processes on next app startup.
/// </summary>
internal sealed class LlamaServerRegistryEntry
{
    public int Pid { get; set; }
    public int Port { get; set; }
    public string ModelKey { get; set; } = string.Empty;
    public string StartTimeUtc { get; set; } = string.Empty;
    /// <summary>Path to llama-server executable; used on macOS for ownership validation when cmdline is unavailable.</summary>
    public string? ServerPath { get; set; }
}
