namespace Mnemo.UI.Services;

public sealed class KeybindActionRouter : IKeybindActionRouter
{
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);

    public void RegisterHandler(string actionId, Action handler)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[actionId] = handler;
    }

    public bool TryExecute(string actionId)
    {
        if (actionId == null) return false;
        if (!_handlers.TryGetValue(actionId, out var handler)) return false;
        handler();
        return true;
    }
}
