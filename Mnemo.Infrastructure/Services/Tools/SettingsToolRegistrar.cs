using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools.Settings;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public static class SettingsToolRegistrar
{
    public static void Register(IFunctionRegistry registry, SettingsToolService svc)
    {
        registry.RegisterTool(new AIToolDefinition("get_setting",
            "Reads an allowlisted setting. Keys from list_settings (e.g. Appearance.Theme); alias \"theme\" maps to Appearance.Theme.",
            typeof(GetSettingParameters), async args => await svc.GetSettingAsync((GetSettingParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("set_setting",
            "Writes an allowlisted setting. Keys from list_settings; alias \"theme\" for Appearance.Theme. Value must match type.",
            typeof(SetSettingParameters), async args => await svc.SetSettingAsync((SetSettingParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("list_settings", "Lists known setting keys (optional category filter).",
            typeof(ListSettingsParameters), async args => await svc.ListSettingsAsync((ListSettingsParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("reset_setting", "Resets a writable setting to its default.",
            typeof(ResetSettingParameters), async args => await svc.ResetSettingAsync((ResetSettingParameters)args).ConfigureAwait(false)));
    }
}
