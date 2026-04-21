using Avalonia.Controls;
using Avalonia.Input;
using Mnemo.UI.Modules.Flashcards.ViewModels;

namespace Mnemo.UI.Modules.Flashcards.Views;

public partial class FlashcardsView : UserControl
{
    public FlashcardsView()
    {
        InitializeComponent();
    }

    private void OnDeckCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || DataContext is not FlashcardsViewModel vm)
            return;

        if (border.DataContext is not FlashcardDeckRowViewModel row)
            return;

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;

        if (vm.OpenDeckCommand.CanExecute(row))
            vm.OpenDeckCommand.Execute(row);

        e.Handled = true;
    }
}
