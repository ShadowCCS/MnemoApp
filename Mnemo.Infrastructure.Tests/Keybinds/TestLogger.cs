using System;
using Mnemo.Core.Enums;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Tests.Keybinds;

public sealed class TestLogger : ILoggerService
{
    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
    }
}
