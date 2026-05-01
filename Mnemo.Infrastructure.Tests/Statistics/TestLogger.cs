using System;
using Mnemo.Core.Enums;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Tests.Statistics;

internal sealed class TestLogger : ILoggerService
{
    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        // Tests don't assert on log output yet; capture is left for future tests.
    }
}
