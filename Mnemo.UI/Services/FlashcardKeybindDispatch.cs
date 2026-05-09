using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia;
using Mnemo.Core.Services;
using Mnemo.UI.Controls;
using Mnemo.UI.Modules.Flashcards;
using Mnemo.UI.Modules.Flashcards.ViewModels;
using Mnemo.UI.Modules.Flashcards.Views;

namespace Mnemo.UI.Services;

public sealed class FlashcardKeybindDispatch(
    INavigationService navigation,
    IFlashcardDeckKeybindContext deckContext) : IFlashcardKeybindDispatch
{
    public bool TrySaveAndAddCard()
    {
        if (navigation.CurrentViewModel is not FlashcardDeckDetailViewModel vm)
            return false;
        var view = deckContext.ActiveView;
        if (view == null || !TryGetDeckFocusedRichEditor(view, out _))
            return false;
        vm.SaveAndAddCardCommand.Execute(null);
        return true;
    }

    public bool TryWrapClozeDeletion()
    {
        if (navigation.CurrentViewModel is not FlashcardDeckDetailViewModel vm || !vm.IsEditorClozeType)
            return false;
        var view = deckContext.ActiveView;
        if (view == null || !TryGetDeckFocusedRichEditor(view, out var editor))
            return false;
        if (!string.Equals(editor.Name, "FrontRichEditor", StringComparison.Ordinal))
            return false;
        var next = FlashcardClozeOrdinal.ComputeNext(vm.EditorFront);
        return editor.TryWrapSelectionWithCloze(next);
    }

    private static bool TryGetDeckFocusedRichEditor(FlashcardDeckDetailView view, out RichDocumentEditor editor)
    {
        editor = null!;
        var top = TopLevel.GetTopLevel(view);
        if (top?.FocusManager?.GetFocusedElement() is not Visual focused)
            return false;
        if (!view.IsVisualAncestorOf(focused))
            return false;
        var rd = focused as RichDocumentEditor ?? focused.FindAncestorOfType<RichDocumentEditor>();
        if (rd == null || !view.IsVisualAncestorOf(rd))
            return false;
        editor = rd;
        return true;
    }
}
