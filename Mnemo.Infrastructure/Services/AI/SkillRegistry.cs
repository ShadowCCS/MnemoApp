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
    private const string CoreSkillId = "Core";

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
            return _skills.Values
                .Select(v => v.Definition)
                .Where(d => !string.Equals(d.Id, CoreSkillId, StringComparison.OrdinalIgnoreCase))
                .ToList();
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
            return new SkillInjectionContext { Tools = MergeCoreInto(Array.Empty<SkillToolDefinition>()) };

        var raw = BuildSkillInjection(skillId);
        if (raw == null)
            return new SkillInjectionContext { Tools = MergeCoreInto(Array.Empty<SkillToolDefinition>()) };

        return new SkillInjectionContext
        {
            SystemPromptFragment = raw.SystemPromptFragment,
            Tools = MergeCoreInto(raw.Tools)
        };
    }

    public SkillInjectionContext GetMergedInjection(IReadOnlyList<string>? skillIds)
    {
        if (skillIds == null || skillIds.Count == 0)
            return new SkillInjectionContext { Tools = MergeCoreInto(Array.Empty<SkillToolDefinition>()) };

        var distinct = new List<string>();
        foreach (var id in skillIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var t = id.Trim();
            if (string.Equals(t, "NONE", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(t, CoreSkillId, StringComparison.OrdinalIgnoreCase)) continue;
            if (distinct.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
            distinct.Add(t);
        }

        if (distinct.Count == 0)
            return new SkillInjectionContext { Tools = MergeCoreInto(Array.Empty<SkillToolDefinition>()) };

        if (distinct.Count == 1)
            return GetInjection(distinct[0]);

        var fragments = new List<string>();
        var tools = new List<SkillToolDefinition>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skillId in distinct)
        {
            var inj = BuildSkillInjection(skillId);
            if (inj == null) continue;
            if (!string.IsNullOrWhiteSpace(inj.SystemPromptFragment))
                fragments.Add(inj.SystemPromptFragment);

            foreach (var t in inj.Tools)
            {
                if (string.IsNullOrWhiteSpace(t.Name))
                    continue;
                if (!seenNames.Add(t.Name))
                {
                    _logger.Debug("SkillRegistry",
                        $"Merged injection: duplicate tool name '{t.Name}' when merging skills; keeping first.");
                    continue;
                }

                tools.Add(t);
            }
        }

        return new SkillInjectionContext
        {
            SystemPromptFragment = fragments.Count == 0 ? null : string.Join("\n\n", fragments),
            Tools = MergeCoreInto(tools)
        };
    }

    public IReadOnlyList<(string SkillId, SkillToolDefinition Tool)> GetAllEnabledManifestTools()
    {
        lock (_lock)
        {
            var list = new List<(string, SkillToolDefinition)>();
            foreach (var kv in _skills)
            {
                var def = kv.Value.Definition;
                if (!def.Injection.IncludeTools)
                    continue;
                foreach (var t in kv.Value.Tools.Tools.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Name)))
                    list.Add((kv.Key, t));
            }

            return list;
        }
    }

    /// <summary>Skill tools and fragment only (no Core merge).</summary>
    private SkillInjectionContext? BuildSkillInjection(string skillId)
    {
        LoadedSkill? loaded;
        lock (_lock)
        {
            _skills.TryGetValue(skillId, out loaded);
        }

        if (loaded == null)
            return null;

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

    private IReadOnlyList<SkillToolDefinition> MergeCoreInto(IReadOnlyList<SkillToolDefinition> tools)
    {
        List<SkillToolDefinition> core;
        lock (_lock)
        {
            if (!_skills.TryGetValue(CoreSkillId, out var coreSkill))
                return tools.ToList();

            core = coreSkill.Tools.Tools.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Name)).ToList();
        }

        if (core.Count == 0)
            return tools.ToList();

        var seen = new HashSet<string>(tools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var list = tools.ToList();
        foreach (var t in core)
        {
            if (!seen.Add(t.Name))
                continue;
            list.Add(t);
        }

        return list;
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
