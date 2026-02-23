using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mnemo.UI.Modules.Notes.ViewModels;

namespace Mnemo.UI.Modules.Notes.Views;

public partial class NoteTreeRow : UserControl
{
    private const double DragStartThreshold = 5.0;
    private PointerPressedEventArgs? _pendingDragPress;
    private Point _pressPosition;
    private bool _dragStarted;
    /// <summary>When set, we'll toggle folder expand on pointer release (avoids fighting TreeViewItem's expand-on-click).</summary>
    private bool? _folderExpandedOnPress;
    private TreeViewItem? _treeViewItem;

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
        // Tunnel with handledEventsToo so we get the press even if TreeViewItem already handled it; then we capture for drag-from-anywhere.
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble);

        _treeViewItem = this.FindAncestorOfType<TreeViewItem>();
        if (_treeViewItem != null)
        {
            UpdateSelectedClass(_treeViewItem.IsSelected);
            _treeViewItem.PropertyChanged += OnTreeViewItemPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_treeViewItem != null)
        {
            _treeViewItem.PropertyChanged -= OnTreeViewItemPropertyChanged;
            _treeViewItem = null;
        }
        if (RowBorder != null)
        {
            RowBorder.Classes.Remove("selected");
            RowBorder.RemoveHandler(Avalonia.Input.DragDrop.DragOverEvent, OnDragOver);
            RowBorder.RemoveHandler(Avalonia.Input.DragDrop.DropEvent, OnDrop);
        }
        RemoveHandler(PointerPressedEvent, OnPointerPressedTunnel);
        RemoveHandler(PointerMovedEvent, OnPointerMoved);
        RemoveHandler(PointerReleasedEvent, OnPointerReleased);
        RemoveHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTreeViewItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TreeViewItem.IsSelectedProperty && _treeViewItem != null)
            UpdateSelectedClass(_treeViewItem.IsSelected);
    }

    private void UpdateSelectedClass(bool isSelected)
    {
        if (RowBorder == null) return;
        if (isSelected)
            RowBorder.Classes.Add("selected");
        else
            RowBorder.Classes.Remove("selected");
    }

    /// <summary>
    /// Tunnel: set selection and prepare for possible drag (capture) so we get the event before any child (e.g. Button) can handle it.
    /// Folders are not selectable: mark event handled so the TreeViewItem never shows selected; toggle expand when clicking the folder row.
    /// </summary>
    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not NoteTreeItemViewModel item) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        var vm = FindNotesViewModel();

        if (item.IsFolder)
        {
            // Prevent TreeView from selecting the folder (stops the folder row from showing as selected).
            e.Handled = true;
            // Defer toggle to pointer release so we use state from before TreeViewItem's expand-on-click runs.
            var container = this.FindAncestorOfType<TreeViewItem>();
            _folderExpandedOnPress = container?.IsExpanded ?? item.IsExpanded;
        }
        else if (vm != null)
        {
            vm.SelectedTreeItem = item;
        }

        // Prepare drag from anywhere: capture here in tunnel so we get move/release even when press was on Button/icon/text.
        var (collection, _) = vm != null ? FindContainingCollection(vm.RootTreeItems, item) : (null, -1);
        if (collection == null) return;

        _pendingDragPress = e;
        _pressPosition = e.GetPosition(this);
        _dragStarted = false;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragPress == null || DataContext is not NoteTreeItemViewModel item) return;

        var current = e.GetPosition(this);
        var delta = current - _pressPosition;
        if (Math.Abs(delta.X) <= DragStartThreshold && Math.Abs(delta.Y) <= DragStartThreshold)
            return;

        var data = new DataObject();
        data.Set(NotesViewModel.NoteTreeItemDragKey, item);
        _ = Avalonia.Input.DragDrop.DoDragDrop(_pendingDragPress, data, DragDropEffects.Move);

        _dragStarted = true;
        _pendingDragPress = null;
        _folderExpandedOnPress = null;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pendingDragPress != null && !_dragStarted && MoreButton != null)
        {
            var pt = e.GetPosition(MoreButton);
            if (pt.X >= 0 && pt.Y >= 0 && pt.X <= MoreButton.Bounds.Width && pt.Y <= MoreButton.Bounds.Height)
                MoreButton.Flyout?.ShowAt(MoreButton);
        }

        // Apply folder expand/collapse toggle on release (state was captured on press, so one click works both ways).
        if (_pendingDragPress != null && !_dragStarted && _folderExpandedOnPress is { } wasExpanded && DataContext is NoteTreeItemViewModel folderItem && folderItem.IsFolder)
        {
            folderItem.IsExpanded = !wasExpanded;
        }
        _folderExpandedOnPress = null;

        e.Pointer.Capture(null);
        ClearPendingDrag();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ClearPendingDrag();
    }

    private void ClearPendingDrag()
    {
        _pendingDragPress = null;
        _dragStarted = false;
        _folderExpandedOnPress = null;
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

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c || c.Tag is not NoteTreeItemViewModel item) return;
        if (!item.IsRenamableFolder) return;
        NameTextBlock!.IsVisible = false;
        NameTextBox!.IsVisible = true;
        NameTextBox.Text = item.Name;
        NameTextBox.CaretIndex = NameTextBox.Text?.Length ?? 0;
        NameTextBox.SelectAll();
        // Defer focus so the flyout can close first
        Dispatcher.UIThread.Post(() => NameTextBox.Focus(), DispatcherPriority.Loaded);
    }

    private void OnNameEditLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitRename();
    }

    private void OnNameEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private async void CommitRename()
    {
        if (!NameTextBox!.IsVisible || DataContext is not NoteTreeItemViewModel item || !item.IsRenamableFolder) return;
        var newName = (NameTextBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(newName)) newName = item.Name;
        item.SetFolderName(newName);
        var vm = FindNotesViewModel();
        if (vm != null)
            await vm.RenameFolderCommand.ExecuteAsync(item);
        NameTextBox.IsVisible = false;
        NameTextBlock!.IsVisible = true;
    }

    private void CancelRename()
    {
        if (!NameTextBox!.IsVisible) return;
        NameTextBox.IsVisible = false;
        NameTextBlock!.IsVisible = true;
    }
}
