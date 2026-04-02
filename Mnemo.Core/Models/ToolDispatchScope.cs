namespace Mnemo.Core.Models;

/// <summary>
/// Optional metadata passed into <see cref="Services.IToolDispatcher.DispatchAsync"/> for ambient context.
/// </summary>
public sealed record ToolDispatchScope(string? ConversationRoutingKey);
