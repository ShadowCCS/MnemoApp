using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MnemoApp.Modules.Notes;

public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();
    }

    private void NoteCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is NoteItemViewModel noteItem)
        {
            var viewModel = DataContext as NotesViewModel;
            viewModel!.SelectedNote = noteItem;
        }
    }

    private void CreateNote_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as NotesViewModel;
        viewModel?.CreateNote();
    }

    private void CreateFolder_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as NotesViewModel;
        viewModel?.CreateFolderPrompt();
    }
}

