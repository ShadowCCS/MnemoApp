using Mnemo.UI.Modules.Flashcards.Views;

namespace Mnemo.UI.Services;

/// <summary>Tracks the visible flashcard deck detail view for keybind dispatch (single active instance).</summary>
public interface IFlashcardDeckKeybindContext
{
    void Attach(FlashcardDeckDetailView view);
    void Detach(FlashcardDeckDetailView view);
    FlashcardDeckDetailView? ActiveView { get; }
}
