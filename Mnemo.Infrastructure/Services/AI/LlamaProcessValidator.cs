using System.Diagnostics;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Validates that a process is a llama-server (by name and on macOS by executable path when available).
/// Ownership for crash recovery is established by registry (PID + port) plus HTTP probe on that port;
/// no CLI flags like --app-tag are required.
/// </summary>
internal static class LlamaProcessValidator
{
    /// <summary>
    /// Returns true if the process with the given PID exists and appears to be a llama-server.
    /// On macOS when cmdline is unavailable, requires executable path to match expectedServerPath if provided.
    /// </summary>
    public static bool IsLlamaProcess(int pid, string? expectedServerPath = null)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            if (string.IsNullOrEmpty(name) || !name.Contains("llama", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!OperatingSystem.IsMacOS())
            {
                return true;
            }

            if (string.IsNullOrEmpty(expectedServerPath))
            {
                return true;
            }

            try
            {
                var exePath = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }

                return PathMatches(expectedServerPath, exePath);
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool PathMatches(string expected, string actual)
    {
        try
        {
            var expectedFull = Path.GetFullPath(expected.Trim());
            var actualFull = Path.GetFullPath(actual.Trim());
            return string.Equals(expectedFull, actualFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
