using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface ISkillRegistry
{
    Task LoadAsync(CancellationToken ct = default);
    Task ReloadAsync(CancellationToken ct = default);

    /// <summary>Drops in-memory skill manifests so disk is not re-read until the next <see cref="LoadAsync"/>.</summary>
    void Unload();
    IReadOnlyList<SkillDefinition> GetEnabledSkills();
    SkillDefinition? TryGet(string id);
    SkillInjectionContext GetInjection(string? skillId);

    /// <summary>
    /// Merges tools (by unique name) and concatenates system fragments for multiple skills.
    /// </summary>
    SkillInjectionContext GetMergedInjection(IReadOnlyList<string>? skillIds);

    /// <summary>
    /// Enabled tools from skill manifests where <c>include_tools</c> is true (for parity checks with <see cref="IFunctionRegistry"/>).
    /// </summary>
    IReadOnlyList<(string SkillId, SkillToolDefinition Tool)> GetAllEnabledManifestTools();
}
