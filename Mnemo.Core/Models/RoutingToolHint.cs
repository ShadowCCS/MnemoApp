namespace Mnemo.Core.Models;

/// <summary>
/// Last tool invocation in a chat thread, fed into routing so short follow-ups (e.g. "add more") can resolve the same skill.
/// </summary>
public sealed record RoutingToolHint(string SkillId, string ToolName, string? Detail);
