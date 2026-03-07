using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mnemo.Core.Models;
using Mnemo.UI.Components.BlockEditor;
using Mnemo.UI.Controls;
using Mnemo.UI.Modules.Notes.ViewModels;

namespace Mnemo.UI.Modules.Notes.Views;

public partial class NotesView : UserControl
{
    private bool _blocksChangedSubscribed;
    private DispatcherTimer? _saveDebounceTimer;
    /// <summary>Note we have pending unsaved block changes for. When flushing on note switch, SelectedNote is already the new note; we must save this one with editor content.</summary>
    private Note? _pendingSaveNote;
    private const int SaveDebounceMs = 500;

    public NotesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not NotesViewModel vm)
            return;

        vm.PropertyChanged -= OnViewModelPropertyChanged;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        // Load blocks if a note is already selected (e.g. after load)
        if (vm.SelectedNote != null)
            Dispatcher.UIThread.Post(() => LoadBlocksForCurrentNote(), DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotesViewModel.SelectedNote))
            return;

        FlushPendingSave();
        Dispatcher.UIThread.Post(() => LoadBlocksForCurrentNote(), DispatcherPriority.Loaded);
    }

    private void FlushPendingSave()
    {
        if (_saveDebounceTimer != null)
        {
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Tick -= OnSaveDebounceTimerTick;
            OnSaveDebounceTimerTick(null, EventArgs.Empty);
        }
    }

    private void LoadBlocksForCurrentNote()
    {
        if (DataContext is not NotesViewModel vm)
            return;

        var editor = this.FindControl<BlockEditor>("NoteBlockEditor");
        if (editor == null)
            return;

        editor.LoadBlocks(vm.GetBlocksForCurrentNote());

        if (!_blocksChangedSubscribed)
        {
            _blocksChangedSubscribed = true;
            editor.BlocksChanged += OnBlockEditorBlocksChanged;
        }
    }

    private void OnBlockEditorBlocksChanged()
    {
        if (DataContext is not NotesViewModel vm || vm.SelectedNote == null)
            return;

        _pendingSaveNote = vm.SelectedNote;
        _saveDebounceTimer?.Stop();
        _saveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SaveDebounceMs)
        };
        _saveDebounceTimer.Tick += OnSaveDebounceTimerTick;
        _saveDebounceTimer.Start();
    }

    private async void OnSaveDebounceTimerTick(object? sender, EventArgs e)
    {
        var timer = _saveDebounceTimer;
        _saveDebounceTimer = null;
        if (timer != null)
        {
            timer.Stop();
            timer.Tick -= OnSaveDebounceTimerTick;
        }

        var noteToSave = _pendingSaveNote;
        _pendingSaveNote = null;

        if (DataContext is not NotesViewModel vm)
            return;

        var editor = this.FindControl<BlockEditor>("NoteBlockEditor");
        if (editor == null)
            return;

        if (noteToSave != null)
            await vm.SaveNoteWithContentAsync(noteToSave, editor.GetBlocks(), null);
        else if (vm.SelectedNote != null)
            await vm.SaveCurrentNoteAsync(editor.GetBlocks(), null);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var titleBox = this.FindControl<TextBox>("NoteTitleBox");
        if (titleBox != null)
            titleBox.LostFocus += OnTitleBoxLostFocus;

        SetupMyNotesDropTargets();
        SetupGutterBoxSelect();
    }

    private void SetupGutterBoxSelect()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scrollViewer == null) return;
        scrollViewer.AddHandler(PointerPressedEvent, OnGutterPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnGutterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount > 1) return;

        var editor = this.FindControl<BlockEditor>("NoteBlockEditor");
        if (editor == null) return;

        // Check if press is in the gutter (outside the editor's own bounds)
        var posInEditor = e.GetPosition(editor);
        var editorBounds = new Rect(0, 0, editor.Bounds.Width, editor.Bounds.Height);
        if (editorBounds.Contains(posInEditor)) return; // inside editor — let it handle

        // Only arm if press is vertically within the editor's vertical extent
        if (posInEditor.Y < 0 || posInEditor.Y > editor.Bounds.Height) return;

        editor.ArmExternalBoxSelect(posInEditor, e.Pointer);
        // Don't mark handled — let ScrollViewer defocus etc. still run
    }

    private void SetupMyNotesDropTargets()
    {
        var header = this.FindControl<Grid>("MyNotesHeader");
        var treeArea = this.FindControl<Border>("MyNotesTreeArea");
        var tree = this.FindControl<MnemoTreeView>("MyNotesTreeView");
        if (header != null)
        {
            DragDrop.SetAllowDrop(header, true);
            header.AddHandler(DragDrop.DragOverEvent, OnMyNotesSectionDragOver, RoutingStrategies.Bubble);
            header.AddHandler(DragDrop.DropEvent, OnMyNotesSectionDrop, RoutingStrategies.Bubble);
        }
        if (treeArea != null)
        {
            DragDrop.SetAllowDrop(treeArea, true);
            treeArea.AddHandler(DragDrop.DragOverEvent, OnMyNotesSectionDragOver, RoutingStrategies.Bubble);
            treeArea.AddHandler(DragDrop.DropEvent, OnMyNotesSectionDrop, RoutingStrategies.Bubble);
        }
        if (tree != null)
        {
            DragDrop.SetAllowDrop(tree, true);
            tree.AddHandler(DragDrop.DragOverEvent, OnMyNotesSectionDragOver, RoutingStrategies.Bubble);
            tree.AddHandler(DragDrop.DropEvent, OnMyNotesSectionDrop, RoutingStrategies.Bubble);
        }
    }

    private void OnMyNotesSectionDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(NotesViewModel.NoteTreeItemDragKey)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private async void OnMyNotesSectionDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not NotesViewModel vm) return;
        if (e.Data.Get(NotesViewModel.NoteTreeItemDragKey) is not NoteTreeItemViewModel source) return;

        // When drop target is tree/treeArea, check if we're over a row: if so, perform drop-into-folder or reorder there
        if (sender is Visual dropTarget && (dropTarget is MnemoTreeView || dropTarget is Border))
        {
            var (target, dropOnFolder, insertAfter) = GetDropTargetRowInfo(dropTarget, e);
            if (target != null)
            {
                await vm.MoveTreeItemAsync(source, target, dropOnFolder, insertAfter);
                e.Handled = true;
                return;
            }
        }

        await vm.MoveTreeItemToRootAsync(source);
    }

    /// <summary>
    /// When drop occurs on the tree/section, find the row under the cursor and return target item plus drop semantics.
    /// Returns (null, false, true) when not over a row.
    /// </summary>
    private static (NoteTreeItemViewModel? target, bool dropOnFolder, bool insertAfter) GetDropTargetRowInfo(Visual dropTarget, DragEventArgs e)
    {
        try
        {
            var pos = e.GetPosition(dropTarget);
            var hit = dropTarget.GetVisualAt(pos);
            var current = hit;
            NoteTreeRow? row = null;
            while (current != null)
            {
                if (current is NoteTreeRow r)
                {
                    row = r;
                    break;
                }
                current = current.GetVisualParent();
            }
            if (row?.DataContext is not NoteTreeItemViewModel target)
                return (null, false, true);

            bool dropOnFolder = target.IsFolder && target.FolderId != null;
            bool insertAfter = true;
            if (!dropOnFolder && row.GetVisualChildren().OfType<Border>().FirstOrDefault() is Border rowBorder)
            {
                try
                {
                    var rowPos = e.GetPosition(rowBorder);
                    var height = Math.Max(rowBorder.Bounds.Height, 1.0);
                    insertAfter = rowPos.Y >= height / 2;
                }
                catch { }
            }
            return (target, dropOnFolder, insertAfter);
        }
        catch { }
        return (null, false, true);
    }

    private static bool IsDropOnTreeRow(Visual dropTarget, DragEventArgs e)
    {
        var (target, _, _) = GetDropTargetRowInfo(dropTarget, e);
        return target != null;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        FlushPendingSave();

        var titleBox = this.FindControl<TextBox>("NoteTitleBox");
        if (titleBox != null)
            titleBox.LostFocus -= OnTitleBoxLostFocus;

        var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
        scrollViewer?.RemoveHandler(PointerPressedEvent, OnGutterPointerPressed);

        var header = this.FindControl<Grid>("MyNotesHeader");
        var treeArea = this.FindControl<Border>("MyNotesTreeArea");
        var tree = this.FindControl<MnemoTreeView>("MyNotesTreeView");
        if (header != null)
        {
            header.RemoveHandler(DragDrop.DragOverEvent, OnMyNotesSectionDragOver);
            header.RemoveHandler(DragDrop.DropEvent, OnMyNotesSectionDrop);
        }
        if (treeArea != null)
        {
            treeArea.RemoveHandler(DragDrop.DragOverEvent, OnMyNotesSectionDragOver);
            treeArea.RemoveHandler(DragDrop.DropEvent, OnMyNotesSectionDrop);
        }
        if (tree != null)
        {
            tree.RemoveHandler(DragDrop.DragOverEvent, OnMyNotesSectionDragOver);
            tree.RemoveHandler(DragDrop.DropEvent, OnMyNotesSectionDrop);
        }

        if (DataContext is NotesViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;

        var editor = this.FindControl<BlockEditor>("NoteBlockEditor");
        if (editor != null && _blocksChangedSubscribed)
        {
            editor.BlocksChanged -= OnBlockEditorBlocksChanged;
            _blocksChangedSubscribed = false;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private async void OnTitleBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NotesViewModel vm || vm.SelectedNote == null)
            return;

        var titleBox = sender as TextBox;
        if (titleBox != null)
            await vm.SaveCurrentNoteAsync(null, titleBox.Text);
    }

}
