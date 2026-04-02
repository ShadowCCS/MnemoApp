using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Resolves and executes tool calls requested by the AI model.
/// Looks up the registered <see cref="AIToolDefinition"/> by name and invokes its handler.
/// </summary>
public interface IToolDispatcher
{
    Task<ToolCallResult> DispatchAsync(ToolCallRequest request, ToolDispatchScope? scope = null, CancellationToken ct = default);
}
