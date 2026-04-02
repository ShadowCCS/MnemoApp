using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Application;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public static class SkillDiscoveryToolRegistrar
{
    public static void Register(IFunctionRegistry registry, SkillDiscoveryToolService svc)
    {
        registry.RegisterTool(new AIToolDefinition("get_skills",
            "Lists all enabled Mnemo skills (id, description, detection hints, whether tools are included for that skill).",
            typeof(EmptyToolParameters), async args => await svc.GetSkillsAsync((EmptyToolParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition("fetch_skill",
            "Returns the system prompt fragment and tool definitions (name, description, parameters JSON) for one skill by skill_id.",
            typeof(FetchSkillParameters), async args => await svc.FetchSkillAsync((FetchSkillParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition("inject_skill",
            "Returns the same injection payload as fetch_skill. When apply_for_conversation is true and the chat has a thread key, stores the skill for subsequent turns so that skill's tools/context replace the routed skill until cleared (skill_id NONE or empty clears).",
            typeof(InjectSkillParameters), async args => await svc.InjectSkillAsync((InjectSkillParameters)args).ConfigureAwait(false)));
    }
}
