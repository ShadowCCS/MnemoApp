using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public sealed class AvaloniaMainThreadDispatcher : IMainThreadDispatcher
{
    public Task InvokeAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);
}
