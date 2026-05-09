namespace Mnemo.Core.Services;

/// <summary>Optional: view models register ephemeral bindings when navigated to.</summary>
public interface IKeybindContributor
{
    void RegisterEphemeralKeybinds(IKeyMap keyMap);
}
