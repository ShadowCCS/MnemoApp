using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Application;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public sealed class SkillDiscoveryToolService
{
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillInjectionOverrideStore _overrideStore;
    private readonly IToolDispatchAmbient _ambient;

    public SkillDiscoveryToolService(
        ISkillRegistry skillRegistry,
        ISkillInjectionOverrideStore overrideStore,
        IToolDispatchAmbient ambient)
    {
        _skillRegistry = skillRegistry;
        _overrideStore = overrideStore;
        _ambient = ambient;
    }

    public async Task<ToolInvocationResult> GetSkillsAsync(EmptyToolParameters _)
    {
        await _skillRegistry.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var skills = _skillRegistry.GetEnabledSkills()
            .Select(s => new
            {
                id = s.Id,
                version = s.Version,
                description = s.Description,
                detection_hint = s.DetectionHint,
                include_tools = s.Injection.IncludeTools
            })
            .ToList();

        return ToolInvocationResult.Success("Enabled skills.", new { skills });
    }

    public async Task<ToolInvocationResult> FetchSkillAsync(FetchSkillParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.SkillId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "skill_id is required.");

        await _skillRegistry.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var sid = p.SkillId.Trim();
        var def = _skillRegistry.TryGet(sid);
        if (def == null && !string.Equals(sid, "NONE", StringComparison.OrdinalIgnoreCase))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"Unknown skill_id '{sid}'.");

        var payload = BuildInjectionPayload(sid);
        return ToolInvocationResult.Success("Skill injection payload.", payload);
    }

    public async Task<ToolInvocationResult> InjectSkillAsync(InjectSkillParameters p)
    {
        await _skillRegistry.LoadAsync(CancellationToken.None).ConfigureAwait(false);

        var convKey = _ambient.ConversationRoutingKey;
        var apply = p.ApplyForConversation && !string.IsNullOrWhiteSpace(convKey);

        if (string.IsNullOrWhiteSpace(p.SkillId)
            || string.Equals(p.SkillId.Trim(), "NONE", StringComparison.OrdinalIgnoreCase))
        {
            if (apply)
                _overrideStore.Set(convKey!, null);

            return ToolInvocationResult.Success(
                "Cleared skill injection override (or NONE).",
                new
                {
                    skill_id = "NONE",
                    applied = apply,
                    conversation_bound = !string.IsNullOrWhiteSpace(convKey),
                    injection = BuildInjectionPayload("NONE")
                });
        }

        var sid = p.SkillId.Trim();
        if (_skillRegistry.TryGet(sid) == null)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"Unknown skill_id '{sid}'.");

        if (apply)
            _overrideStore.Set(convKey!, sid);

        return ToolInvocationResult.Success(
            apply ? $"Skill injection override set to '{sid}'." : $"Skill '{sid}' payload (not persisted for conversation).",
            new
            {
                skill_id = sid,
                applied = apply,
                conversation_bound = !string.IsNullOrWhiteSpace(convKey),
                injection = BuildInjectionPayload(sid)
            });
    }

    private object BuildInjectionPayload(string skillId)
    {
        var ctx = _skillRegistry.GetInjection(skillId);
        var tools = ctx.Tools
            .Where(t => t.Enabled)
            .Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parameters_json = t.Parameters.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : t.Parameters.GetRawText()
            })
            .ToList();

        return new
        {
            skill_id = string.IsNullOrWhiteSpace(skillId) || string.Equals(skillId, "NONE", StringComparison.OrdinalIgnoreCase)
                ? "NONE"
                : skillId,
            system_prompt_fragment = ctx.SystemPromptFragment,
            tools
        };
    }
}
