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
            "Lists main skill ids (Notes, Mindmap, …). Heavy analytics/statistics tooling is omitted—use get_analytics_skills then inject_skill. Does not enable module tools by itself.",
            typeof(EmptyToolParameters), async args => await svc.GetSkillsAsync((EmptyToolParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition("fetch_skill",
            "Preview-only: returns fragment + tool schemas. Does not enable calling those tools; use inject_skill to run list_notes and other module tools. Hidden-catalog ids (e.g. Analytics) are listed by get_analytics_skills.",
            typeof(FetchSkillParameters), async args => await svc.FetchSkillAsync((FetchSkillParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition("inject_skill",
            "Required to use a module's tools (e.g. list_notes for Notes). Heavy statistics/analytics tools are not listed by get_skills—call get_analytics_skills then inject_skill with that skill id (e.g. Analytics). Same payload shape as fetch_skill but applies for this conversation. navigate_to does not load tools.",
            typeof(InjectSkillParameters), async args => await svc.InjectSkillAsync((InjectSkillParameters)args).ConfigureAwait(false)));

        registry.RegisterTool(new AIToolDefinition("get_analytics_skills",
            "Lists skills intentionally omitted from get_skills (many stats_* tools). Read-only discovery—does not enable them. Next step: inject_skill with the returned skill id (typically Analytics).",
            typeof(EmptyToolParameters), async args => await svc.GetAnalyticsSkillsAsync((EmptyToolParameters)args).ConfigureAwait(false)));
    }
}
