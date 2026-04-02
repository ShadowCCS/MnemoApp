namespace Mnemo.Core.Models.Tools;

/// <summary>
/// Typed outcome of a tool handler. Serialized for the model via <see cref="IToolResultFormatter"/>.
/// </summary>
public sealed class ToolInvocationResult
{
    public bool Ok { get; init; }
    public string Code { get; init; } = ToolResultCodes.Success;
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }

    public static ToolInvocationResult Success(string message = "OK", object? data = null) =>
        new() { Ok = true, Code = ToolResultCodes.Success, Message = message, Data = data };

    public static ToolInvocationResult Failure(string code, string message, object? data = null) =>
        new() { Ok = false, Code = code, Message = message, Data = data };
}
