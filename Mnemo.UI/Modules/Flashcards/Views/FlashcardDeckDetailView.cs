using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mnemo.UI.Modules.Flashcards.ViewModels;

namespace Mnemo.UI.Modules.Flashcards.Views;

public partial class FlashcardDeckDetailView : UserControl
{
    public FlashcardDeckDetailView()
    {
        InitializeComponent();
    }

    private void OnInsertClozeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control source || DataContext is not FlashcardDeckDetailViewModel vm)
            return;

        var shell = source.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("fc-card-editor-shell"));
        var frontBox = shell?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("fc-editor-front"));

        if (frontBox is null)
            return;

        var text = frontBox.Text ?? string.Empty;
        var start = Math.Min(frontBox.SelectionStart, frontBox.SelectionEnd);
        var end = Math.Max(frontBox.SelectionStart, frontBox.SelectionEnd);

        var (newText, caret) = FlashcardDeckDetailViewModel.BuildFrontWithClozeInserted(text, start, end);
        vm.EditorFront = newText;

        Dispatcher.UIThread.Post(() =>
        {
            frontBox.Focus();
            frontBox.CaretIndex = caret;
            frontBox.SelectionStart = caret;
            frontBox.SelectionEnd = caret;
        }, DispatcherPriority.Input);
    }
}
