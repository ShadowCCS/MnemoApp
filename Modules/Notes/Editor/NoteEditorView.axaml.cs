using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace MnemoApp.Modules.Notes.Editor;

public partial class NoteEditorView : UserControl
{
    public NoteEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private NoteEditorViewModel? _currentViewModel;
    
    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from previous view model
        if (_currentViewModel != null && BlockEditorControl != null)
        {
            BlockEditorControl.BlocksChanged -= _currentViewModel.OnBlocksChanged;
        }
        
        if (DataContext is NoteEditorViewModel viewModel && BlockEditorControl != null)
        {
            _currentViewModel = viewModel;
            
            // Wire up block editor instance
            viewModel.BlockEditorInstance = BlockEditorControl;
            BlockEditorControl.BlocksChanged += viewModel.OnBlocksChanged;

            // Load note data if noteId is passed
            if (!string.IsNullOrEmpty(viewModel.NoteId))
            {
                await viewModel.LoadNoteAsync(viewModel.NoteId);
            }
        }
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as NoteEditorViewModel;
        viewModel?.DeleteNote();
    }
}

