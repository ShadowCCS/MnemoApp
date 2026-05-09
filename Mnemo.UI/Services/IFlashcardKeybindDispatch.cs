namespace Mnemo.UI.Services;

public interface IFlashcardKeybindDispatch
{
    /// <summary>Returns false when the chord should fall through (e.g. not on deck editor).</summary>
    bool TrySaveAndAddCard();

    bool TryWrapClozeDeletion();
}
