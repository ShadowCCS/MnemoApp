using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Keybinds;

public sealed class KeybindManifestCollector : IKeybindManifestRegistry
{
    private readonly List<KeybindActionDefinition> _list = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public void Register(KeybindActionDefinition definition)
    {
        if (!_ids.Add(definition.ActionId))
        {
#if DEBUG
            throw new InvalidOperationException($"Duplicate keybind action id '{definition.ActionId}'. First registration wins in release.");
#else
            return;
#endif
        }

        _list.Add(definition);
    }

    public IReadOnlyList<KeybindActionDefinition> GetAll() => _list;
}
