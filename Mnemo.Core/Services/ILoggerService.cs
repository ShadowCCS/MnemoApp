using System;
using Mnemo.Core.Enums;

namespace Mnemo.Core.Services;

public interface ILoggerService
{
    void Log(LogLevel level, string category, string message, Exception? exception = null);
    void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
    void Info(string category, string message) => Log(LogLevel.Info, category, message);
    void Warning(string category, string message) => Log(LogLevel.Warning, category, message);
    void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);
    void Critical(string category, string message, Exception? ex = null) => Log(LogLevel.Critical, category, message, ex);
}

