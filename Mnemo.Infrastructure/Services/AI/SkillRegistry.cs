using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public sealed class SkillRegistry : ISkillRegistry
{
    private readonly ILoggerService _logger;
    private readonly object _lock = new();
    private Dictionary<string, LoadedSkill> _skills = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _loaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SkillRegistry(ILoggerService logger)
    {
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded)
            return;

        await ReloadAsync(ct).ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var loaded = await LoadFromDiskAsync(ct).ConfigureAwait(false);
        lock (_lock)
        {
            _skills = loaded;
            _loaded = true;
        }
    }

    public IReadOnlyList<SkillDefinition> GetEnabledSkills()
    {
        lock (_lock)
        {
            return _skills.Values.Select(v => v.Definition).ToList();
        }
    }

    public SkillDefinition? TryGet(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        lock (_lock)
        {
            return _skills.TryGetValue(id, out var loaded) ? loaded.Definition : null;
        }
    }

    public SkillInjectionContext GetInjection(string? skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId) || string.Equals(skillId, "NONE", StringComparison.OrdinalIgnoreCase))
            return new SkillInjectionContext();

        LoadedSkill? loaded;
        lock (_lock)
        {
            _skills.TryGetValue(skillId, out loaded);
        }

        if (loaded == null)
            return new SkillInjectionContext();

        var includeTools = loaded.Definition.Injection.IncludeTools;
        var enabledTools = includeTools
            ? loaded.Tools.Tools.Where(t => t.Enabled).ToList()
            : [];

        return new SkillInjectionContext
        {
            SystemPromptFragment = loaded.Definition.Injection.SystemPromptFragment,
            Tools = enabledTools
        };
    }

    private async Task<Dictionary<string, LoadedSkill>> LoadFromDiskAsync(CancellationToken ct)
    {
        var skills = new Dictionary<string, LoadedSkill>(StringComparer.OrdinalIgnoreCase);
        var skillsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skills");

        if (!Directory.Exists(skillsDir))
        {
            _logger.Warning("SkillRegistry", $"Skills directory not found: {skillsDir}");
            return skills;
        }

        foreach (var folder in Directory.EnumerateDirectories(skillsDir))
        {
            ct.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(folder);
            var skillPath = Path.Combine(folder, "skill.json");
            if (!File.Exists(skillPath))
                continue;

            try
            {
                await using var skillStream = File.OpenRead(skillPath);
                var definition = await JsonSerializer.DeserializeAsync<SkillDefinition>(skillStream, JsonOptions, ct).ConfigureAwait(false);
                if (definition == null || !definition.Enabled || string.IsNullOrWhiteSpace(definition.Id))
                    continue;

                if (!string.Equals(definition.Id, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warning("SkillRegistry", $"Skill id '{definition.Id}' does not match folder '{folderName}'. Skipping.");
                    continue;
                }

                var toolsPath = Path.Combine(folder, "tools.json");
                var tools = new SkillToolSchema();
                if (File.Exists(toolsPath))
                {
                    await using var toolsStream = File.OpenRead(toolsPath);
                    tools = await JsonSerializer.DeserializeAsync<SkillToolSchema>(toolsStream, JsonOptions, ct).ConfigureAwait(false)
                        ?? new SkillToolSchema();
                }

                skills[definition.Id] = new LoadedSkill(definition, tools);
            }
            catch (Exception ex)
            {
                _logger.Warning("SkillRegistry", $"Failed to load skill from '{folderName}': {ex.Message}");
            }
        }

        _logger.Info("SkillRegistry", $"Loaded {skills.Count} enabled skills.");
        return skills;
    }

    private sealed record LoadedSkill(SkillDefinition Definition, SkillToolSchema Tools);
}
