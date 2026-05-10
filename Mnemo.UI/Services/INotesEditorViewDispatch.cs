namespace Mnemo.UI.Services;

/// <summary>Invokes notes editor chrome actions (zoom/scroll) when the Notes route is active.</summary>
public interface INotesEditorViewDispatch
{
    /// <summary>Resets zoom and scroll on the visible notes editor when the current route is Notes.</summary>
    /// <returns><c>true</c> if a notes view handled the request.</returns>
    bool TryResetEditorView();
}
