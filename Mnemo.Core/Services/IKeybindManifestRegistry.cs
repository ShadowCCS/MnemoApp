using Mnemo.Core.Models.Keybinds;

namespace Mnemo.Core.Services;

/// <summary>Collects static keybind definitions during module bootstrap.</summary>
public interface IKeybindManifestRegistry
{
    void Register(KeybindActionDefinition definition);
    IReadOnlyList<KeybindActionDefinition> GetAll();
}
