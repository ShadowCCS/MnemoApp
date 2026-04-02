using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Application;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public static class ApplicationToolRegistrar
{
    public static void Register(IFunctionRegistry registry, ApplicationToolService svc)
    {
        registry.RegisterTool(new AIToolDefinition("get_version", "Returns application assembly version.",
            typeof(EmptyToolParameters), async args => await svc.GetVersionAsync((EmptyToolParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("get_current_route", "Returns current navigation route.",
            typeof(EmptyToolParameters), async args => await svc.GetCurrentRouteAsync((EmptyToolParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("navigate_to",
            "Navigates to a module route: overview, notes, chat, mindmap, path, settings. Optional entity_id passed to route.",
            typeof(NavigateToParameters), async args => await svc.NavigateToAsync((NavigateToParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("open_settings", "Opens Settings (optional section hint).",
            typeof(OpenSettingsParameters), async args => await svc.OpenSettingsAsync((OpenSettingsParameters)args).ConfigureAwait(false)));
    }
}
