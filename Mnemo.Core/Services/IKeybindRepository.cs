using Mnemo.Core.Models.Keybinds;

namespace Mnemo.Core.Services;

public interface IKeybindRepository
{
    Task<IReadOnlyDictionary<string, KeybindOverrideDocument>> LoadOverridesAsync(CancellationToken cancellationToken = default);
    Task SaveOverrideAsync(string actionId, KeybindOverrideDocument document, CancellationToken cancellationToken = default);
    Task DeleteOverrideAsync(string actionId, CancellationToken cancellationToken = default);
    Task ClearAllOverridesAsync(CancellationToken cancellationToken = default);
}
