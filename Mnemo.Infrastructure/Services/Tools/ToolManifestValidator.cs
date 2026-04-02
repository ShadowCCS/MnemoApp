using System;
using System.Linq;
using System.Text.Json;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

/// <summary>Validates skill tool manifests vs registered runtime handlers and basic JSON Schema shape.</summary>
public static class ToolManifestValidator
{
    public static void ValidateAndLog(ISkillRegistry skills, IFunctionRegistry registry, ILoggerService logger)
    {
        var handlers = registry.GetTools()
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var (skillId, tool) in skills.GetAllEnabledManifestTools())
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                logger.Warning("ToolManifestValidator", $"Skill '{skillId}' has a tool with empty name.");
                continue;
            }

            if (!handlers.ContainsKey(tool.Name))
            {
                logger.Warning("ToolManifestValidator",
                    $"Skill '{skillId}' enables tool '{tool.Name}' but no handler is registered.");
                continue;
            }

            if (!IsValidParametersObject(tool.Parameters))
            {
                logger.Warning("ToolManifestValidator",
                    $"Skill '{skillId}' tool '{tool.Name}' has invalid parameters schema (expected JSON object with optional type/properties).");
            }
        }

        var manifestNames = skills.GetAllEnabledManifestTools()
            .Select(t => t.Tool.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var def in registry.GetTools())
        {
            if (!manifestNames.Contains(def.Name))
                logger.Warning("ToolManifestValidator",
                    $"Registered handler '{def.Name}' is not listed as an enabled tool in any skill manifest.");
        }
    }

    private static bool IsValidParametersObject(JsonElement parameters)
    {
        if (parameters.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (parameters.ValueKind != JsonValueKind.Object)
            return false;
        return true;
    }
}
