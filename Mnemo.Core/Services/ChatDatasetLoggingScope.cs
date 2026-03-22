using System;
using System.Threading;

namespace Mnemo.Core.Services;

/// <summary>
/// Correlates orchestration (manager) and chat streaming for dataset logging within one user send.
/// </summary>
public static class ChatDatasetLoggingScope
{
    private static readonly AsyncLocal<string?> TurnId = new();

    public static string? CurrentTurnId => TurnId.Value;

    /// <summary>Begins a turn; dispose to clear. Call from the same async flow as AnalyzeMessageAsync / streaming.</summary>
    public static IDisposable BeginTurn(out string turnId)
    {
        turnId = Guid.NewGuid().ToString("N");
        TurnId.Value = turnId;
        return new ClearScope(turnId);
    }

    private sealed class ClearScope : IDisposable
    {
        private readonly string _id;
        private bool _disposed;

        public ClearScope(string id) => _id = id;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (TurnId.Value == _id)
                TurnId.Value = null;
        }
    }
}
