using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Resolves tool calls from the AI model against the runtime <see cref="IFunctionRegistry"/>
/// and invokes the matching handler.
/// </summary>
public sealed class ToolDispatcher : IToolDispatcher
{
    private readonly IFunctionRegistry _registry;
    private readonly ILoggerService _logger;
    private readonly IToolResultFormatter _formatter;
    private readonly IToolDispatchAmbient _ambient;

    public ToolDispatcher(
        IFunctionRegistry registry,
        ILoggerService logger,
        IToolResultFormatter formatter,
        IToolDispatchAmbient ambient)
    {
        _registry = registry;
        _logger = logger;
        _formatter = formatter;
        _ambient = ambient;
    }

    public async Task<ToolCallResult> DispatchAsync(ToolCallRequest request, ToolDispatchScope? scope = null, CancellationToken ct = default)
    {
        var tool = _registry.GetTools()
            .FirstOrDefault(t => string.Equals(t.Name, request.Name, StringComparison.OrdinalIgnoreCase));

        if (tool == null)
        {
            _logger.Warning("ToolDispatcher", $"No handler registered for tool '{request.Name}'.");
            return new ToolCallResult(request.Id, request.Name, $"Error: tool '{request.Name}' is not available.");
        }

        using (_ambient.Enter(scope?.ConversationRoutingKey))
        {
            try
            {
                _logger.Info("ToolDispatcher", $"Dispatching tool '{request.Name}' with args: {request.ArgumentsJson}");

                object parameters;
                try
                {
                    parameters = JsonSerializer.Deserialize(request.ArgumentsJson, tool.ParametersType,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? Activator.CreateInstance(tool.ParametersType)!;
                }
                catch (JsonException ex)
                {
                    _logger.Warning("ToolDispatcher", $"Failed to deserialize args for '{request.Name}': {ex.Message}");
                    return new ToolCallResult(request.Id, request.Name, $"Error: invalid arguments — {ex.Message}");
                }

                ct.ThrowIfCancellationRequested();

                var correlation = Guid.NewGuid().ToString("N")[..12];
                var result = await tool.Handler(parameters).ConfigureAwait(false);
                var content = _formatter.Format(result);
                _logger.Info("ToolDispatcher", $"[{correlation}] Tool '{request.Name}' completed ({result.Code}).");
                return new ToolCallResult(request.Id, request.Name, content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("ToolDispatcher", $"Tool '{request.Name}' threw an exception.", ex);
                return new ToolCallResult(request.Id, request.Name, $"Error: {ex.Message}");
            }
        }
    }
}
