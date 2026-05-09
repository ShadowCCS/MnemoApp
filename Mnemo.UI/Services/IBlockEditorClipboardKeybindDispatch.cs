namespace Mnemo.UI.Services;

/// <summary>Runs block-editor clipboard shortcuts resolved via <see cref="Mnemo.Core.Services.IKeyMap"/> (notes / flashcard body).</summary>
public interface IBlockEditorClipboardKeybindDispatch
{
    bool TryCopy();

    bool TryCut();

    bool TryPaste();
}
