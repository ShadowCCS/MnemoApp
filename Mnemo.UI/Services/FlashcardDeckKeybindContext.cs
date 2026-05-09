using Mnemo.UI.Modules.Flashcards.Views;

namespace Mnemo.UI.Services;

public sealed class FlashcardDeckKeybindContext : IFlashcardDeckKeybindContext
{
    private FlashcardDeckDetailView? _view;

    public FlashcardDeckDetailView? ActiveView => _view;

    public void Attach(FlashcardDeckDetailView view) => _view = view;

    public void Detach(FlashcardDeckDetailView view)
    {
        if (_view == view)
            _view = null;
    }
}
