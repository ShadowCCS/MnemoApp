using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Tests.Keybinds;

public sealed class FakeKeybindRepository : IKeybindRepository
{
    public Dictionary<string, KeybindOverrideDocument> Store { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, KeybindOverrideDocument>> LoadOverridesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, KeybindOverrideDocument>>(
            new Dictionary<string, KeybindOverrideDocument>(Store, StringComparer.Ordinal));

    public Task SaveOverrideAsync(string actionId, KeybindOverrideDocument document, CancellationToken cancellationToken = default)
    {
        Store[actionId] = document;
        return Task.CompletedTask;
    }

    public Task DeleteOverrideAsync(string actionId, CancellationToken cancellationToken = default)
    {
        Store.Remove(actionId);
        return Task.CompletedTask;
    }

    public Task ClearAllOverridesAsync(CancellationToken cancellationToken = default)
    {
        Store.Clear();
        return Task.CompletedTask;
    }
}
