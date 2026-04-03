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
            "Lists skill ids (Notes, Mindmap, …). Does not add module tools by itself; follow with inject_skill to enable those tools.",
            typeof(EmptyToolParameters), async args => await svc.GetSkillsAsync((EmptyToolParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition("fetch_skill",
            "Preview-only: returns fragment + tool schemas. Does not enable calling those tools; use inject_skill to run list_notes and other module tools.",
            typeof(FetchSkillParameters), async args => await svc.FetchSkillAsync((FetchSkillParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition("inject_skill",
            "Required to use a module's tools (e.g. list_notes for Notes). Same payload as fetch_skill but applies skill for this conversation. navigate_to is not a substitute.",
            typeof(InjectSkillParameters), async args => await svc.InjectSkillAsync((InjectSkillParameters)args).ConfigureAwait(false)));
    }
}
