using System;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

/// <summary>Marshals work to the UI main thread when tool handlers touch UI-bound services.</summary>
public interface IMainThreadDispatcher
{
    Task InvokeAsync(Func<Task> action);
}
