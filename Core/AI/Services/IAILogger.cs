using System;

namespace MnemoApp.Core.AI.Services
{
    /// <summary>
    /// Simple logging interface for AI subsystem
    /// </summary>
    public interface IAILogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
    }

    /// <summary>
    /// Simple debug logger implementation
    /// </summary>
    public class DebugAILogger : IAILogger
    {
        public void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[AI DEBUG] {message}");
        }

        public void LogInfo(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[AI INFO] {message}");
        }

        public void LogWarning(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[AI WARNING] {message}");
        }

        public void LogError(string message, Exception? exception = null)
        {
            System.Diagnostics.Debug.WriteLine($"[AI ERROR] {message}");
            if (exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"[AI ERROR] Exception: {exception}");
            }
        }
    }
}
