namespace Mnemo.UI.Services;

/// <summary>Executes keybind actions by id; matching is owned by <see cref="Mnemo.Core.Services.IKeyMap"/>.</summary>
public interface IKeybindActionRouter
{
    void RegisterHandler(string actionId, Action handler);
    /// <summary>When the handler returns false, the key chord is not treated as handled (event continues routing).</summary>
    void RegisterHandler(string actionId, Func<bool> handler);
    bool TryExecute(string actionId);
}
