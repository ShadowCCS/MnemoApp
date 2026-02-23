using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Mnemo.UI.Modules.Notes.ViewModels;

namespace Mnemo.UI.Modules.Notes.Views;

public partial class NoteTreeRow : UserControl
{
    public NoteTreeRow()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (RowBorder == null) return;

        Avalonia.Input.DragDrop.SetAllowDrop(RowBorder, true);
        RowBorder.AddHandler(Avalonia.Input.DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        RowBorder.AddHandler(Avalonia.Input.DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
        RowBorder.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (RowBorder != null)
        {
            RowBorder.RemoveHandler(Avalonia.Input.DragDrop.DragOverEvent, OnDragOver);
            RowBorder.RemoveHandler(Avalonia.Input.DragDrop.DropEvent, OnDrop);
            RowBorder.RemoveHandler(PointerPressedEvent, OnPointerPressed);
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not NoteTreeItemViewModel item) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != Avalonia.Input.PointerUpdateKind.LeftButtonPressed) return;

        var vm = FindNotesViewModel();
        if (vm == null) return;
        // Only allow drag from "My Notes" tree (not from Favourites)
        var (collection, _) = FindContainingCollection(vm.RootTreeItems, item);
        if (collection == null) return;

        var data = new DataObject();
        data.Set(NotesViewModel.NoteTreeItemDragKey, item);
        Avalonia.Input.DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(NotesViewModel.NoteTreeItemDragKey))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        var vm = FindNotesViewModel();
        if (vm == null) { e.DragEffects = DragDropEffects.None; return; }
        // Only allow drop on "My Notes" tree
        if (DataContext is NoteTreeItemViewModel target)
        {
            var (collection, _) = FindContainingCollection(vm.RootTreeItems, target);
            e.DragEffects = collection != null ? DragDropEffects.Move : DragDropEffects.None;
        }
        else
            e.DragEffects = DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not NoteTreeItemViewModel target) return;
        if (e.Data.Get(NotesViewModel.NoteTreeItemDragKey) is not NoteTreeItemViewModel source) return;

        var vm = FindNotesViewModel();
        if (vm == null) return;

        // If target is a folder, dropping anywhere within its row = drop into folder. Otherwise reorder (insert before/after by vertical position).
        bool dropOnFolder = target.IsFolder && target.FolderId != null;
        bool insertAfter = true;
        if (!dropOnFolder && RowBorder != null)
        {
            try
            {
                var pos = e.GetPosition(RowBorder);
                var height = Math.Max(RowBorder.Bounds.Height, 1.0);
                insertAfter = pos.Y >= height / 2;
            }
            catch { }
        }

        await vm.MoveTreeItemAsync(source, target, dropOnFolder, insertAfter);
        e.Handled = true;
    }

    private NotesViewModel? FindNotesViewModel()
    {
        var current = this as Visual;
        while (current != null)
        {
            if (current is Control c && c.DataContext is NotesViewModel vm)
                return vm;
            current = current.GetVisualParent();
        }
        return null;
    }

    private static (ObservableCollection<NoteTreeItemViewModel>? collection, int index) FindContainingCollection(
        ObservableCollection<NoteTreeItemViewModel> root,
        NoteTreeItemViewModel item)
    {
        for (var i = 0; i < root.Count; i++)
        {
            if (ReferenceEquals(root[i], item))
                return (root, i);
            var (col, idx) = FindContainingCollection(root[i].Children, item);
            if (col != null) return (col, idx);
        }
        return (null, -1);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c || c.Tag is not NoteTreeItemViewModel item) return;
        var vm = FindNotesViewModel();
        if (vm == null) return;
        if (item.IsFolder)
            await vm.DeleteFolderCommand.ExecuteAsync(item);
        else
            await vm.DeleteNoteCommand.ExecuteAsync(item);
    }
}
