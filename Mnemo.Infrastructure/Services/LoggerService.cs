using System;
using System.IO;
using Mnemo.Core.Enums;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class LoggerService : ILoggerService
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public LoggerService()
    {
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
        _logFilePath = Path.Combine(logDir, $"log_{DateTime.Now:yyyyMMdd}.txt");
    }

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logMessage = $"[{timestamp}] [{level}] [{category}] {message}";
        
        if (exception != null)
        {
            logMessage += $"{Environment.NewLine}Exception: {exception}";
        }

        // Console Sink
        Console.WriteLine(logMessage);
        
        // File Sink
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // If file logging fails, at least show it on console
                Console.WriteLine($"[FATAL] Failed to write to log file: {ex.Message}");
            }
        }
    }
}

