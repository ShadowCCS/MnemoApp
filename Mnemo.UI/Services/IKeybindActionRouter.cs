namespace Mnemo.UI.Services;

/// <summary>Executes keybind actions by id; matching is owned by <see cref="Mnemo.Core.Services.IKeyMap"/>.</summary>
public interface IKeybindActionRouter
{
    void RegisterHandler(string actionId, Action handler);
    bool TryExecute(string actionId);
}
