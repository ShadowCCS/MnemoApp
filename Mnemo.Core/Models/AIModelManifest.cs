using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>
/// Per-model install metadata. Keep <c>manifest.json</c> minimal: identity, type, and tuning via <see cref="Metadata"/>.
/// <see cref="Role"/>, <see cref="Endpoint"/>, and <see cref="LocalPath"/> are assigned at load time from the folder layout (see <see cref="AIModelRoles"/>).
/// </summary>
public class AIModelManifest
{
    /// <summary>Stable id for this install (used in registry and server bookkeeping).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable label for logs/UI; optional in JSON if the registry fills a default from the tier folder.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public AIModelType Type { get; set; }

    /// <summary>Absolute path to the model directory; set by the registry, not stored in JSON.</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>Chat template key (CHATML, LLAMA3, …). Heuristics may set this from <see cref="DisplayName"/> when omitted.</summary>
    public string PromptTemplate { get; set; } = "ChatML";

    /// <summary>HTTP base URL for llama-server; defaulted from tier port when omitted in JSON.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Tier / slot: <see cref="AIModelRoles"/>; derived from the parent folder name, not stored in JSON.</summary>
    public string? Role { get; set; }

    /// <summary>
    /// Optional server flags: Temperature, MaxTokens, GpuLayers, ContextSize, FlashAttn, PreferCpu, ForceGpu, etc.
    /// PreferCpu/ForceGpu adjust GPU offload when <c>AI.GpuAcceleration</c> is unset or per-model overrides are needed.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
