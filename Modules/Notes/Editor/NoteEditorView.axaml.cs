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
    
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from previous view model
        if (_currentViewModel != null && BlockEditorControl != null)
        {
            BlockEditorControl.BlocksChanged -= _currentViewModel.OnBlocksChanged;
        }
        
        if (DataContext is NoteEditorViewModel viewModel && BlockEditorControl != null)
        {
            _currentViewModel = viewModel;
            
            System.Diagnostics.Debug.WriteLine("[NoteEditorView] Subscribing to BlocksChanged event");
            
            // Wire up block editor instance
            // This will trigger LoadNoteAsync if NoteId is already set (via the setter)
            viewModel.BlockEditorInstance = BlockEditorControl;
            BlockEditorControl.BlocksChanged += viewModel.OnBlocksChanged;
            
            System.Diagnostics.Debug.WriteLine("[NoteEditorView] BlocksChanged subscription complete");
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as NoteEditorViewModel;
        if (viewModel != null)
            await viewModel.DeleteNoteAsync();
    }
}

