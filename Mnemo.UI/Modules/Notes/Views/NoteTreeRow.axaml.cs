using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mnemo.UI.Controls;
using Mnemo.UI.Modules.Notes.ViewModels;

namespace Mnemo.UI.Modules.Notes.Views;

public partial class NoteTreeRow : UserControl
{
    private const double DragStartThreshold = 5.0;
    private const string FolderItemClass = "folder-item";
    private PointerPressedEventArgs? _pendingDragPress;
    private Point _pressPosition;
    private bool _dragStarted;
    private MnemoTreeViewItem? _treeViewItem;
    private EventHandler<PointerPressedEventArgs>? _folderPointerPressedHandler;

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

        _treeViewItem = this.FindAncestorOfType<MnemoTreeViewItem>();
        if (_treeViewItem != null)
        {
            UpdateSelectedClass(_treeViewItem.IsSelected);
            _treeViewItem.PropertyChanged += OnTreeViewItemPropertyChanged;
            if (DataContext is NoteTreeItemViewModel item && item.IsFolder)
            {
                _treeViewItem.Classes.Add(FolderItemClass);
                _folderPointerPressedHandler = OnFolderTreeViewItemPointerPressedTunnel;
                _treeViewItem.AddHandler(PointerPressedEvent, _folderPointerPressedHandler, RoutingStrategies.Tunnel, handledEventsToo: true);
            }
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_treeViewItem != null)
        {
            _treeViewItem.PropertyChanged -= OnTreeViewItemPropertyChanged;
            if (_folderPointerPressedHandler != null)
            {
                _treeViewItem.RemoveHandler(PointerPressedEvent, _folderPointerPressedHandler);
                _folderPointerPressedHandler = null;
            }
            _treeViewItem.Classes.Remove(FolderItemClass);
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
        if (e.Property == MnemoTreeViewItem.IsSelectedProperty && _treeViewItem != null)
            UpdateSelectedClass(_treeViewItem.IsSelected);
    }

    private void UpdateSelectedClass(bool isSelected)
    {
        if (RowBorder == null) return;
        // Folders are not selectable: never show selected state for folder rows.
        if (DataContext is NoteTreeItemViewModel item && item.IsFolder)
        {
            RowBorder.Classes.Remove("selected");
            return;
        }
        if (isSelected)
            RowBorder.Classes.Add("selected");
        else
            RowBorder.Classes.Remove("selected");
    }

    /// <summary>
    /// Called on the TreeViewItem (Tunnel) for folder rows only. Only toggle when the click is on the folder header, not on a child item.
    /// </summary>
    private void OnFolderTreeViewItemPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (sender is not MnemoTreeViewItem tvi || tvi.DataContext is not NoteTreeItemViewModel item || !item.IsFolder) return;
        // Only handle if the click was on this folder's header row. If the hit element is inside a child item, do not handle.
        if (e.Source is Visual source && source.FindAncestorOfType<MnemoTreeViewItem>() is MnemoTreeViewItem hitTreeViewItem && hitTreeViewItem != tvi)
            return;
        e.Handled = true;
        item.IsExpanded = !item.IsExpanded;
    }

    /// <summary>
    /// Tunnel: set selection and prepare for possible drag (capture). For folders we only mark handled (toggle is done on TreeViewItem).
    /// </summary>
    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not NoteTreeItemViewModel item) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        var vm = FindNotesViewModel();

        if (item.IsFolder)
            e.Handled = true;
        else if (vm != null)
        {
            vm.SelectedTreeItem = item;
        }

        // Prepare drag from anywhere: capture here in tunnel so we get move/release even when press was on Button/icon/text.
        var (collection, _) = vm != null ? FindContainingCollectionInAnyTree(vm, item) : (null, -1);
        if (collection == null) return;

        _pendingDragPress = e;
        _pressPosition = e.GetPosition(this);
        _dragStarted = false;
        e.Handled = true; // Prevent TreeViewItem/PressedMixin from capturing pointer so we receive PointerMoved.
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
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pendingDragPress != null && !_dragStarted && MoreButton != null)
        {
            var pt = e.GetPosition(MoreButton);
            if (pt.X >= 0 && pt.Y >= 0 && pt.X <= MoreButton.Bounds.Width && pt.Y <= MoreButton.Bounds.Height)
                MoreButton.Flyout?.ShowAt(MoreButton);
        }

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

    /// <summary>Finds the collection containing the item in either RootTreeItems or FavouriteNotes.</summary>
    private static (ObservableCollection<NoteTreeItemViewModel>? collection, int index) FindContainingCollectionInAnyTree(
        NotesViewModel vm,
        NoteTreeItemViewModel item)
    {
        var (col, idx) = FindContainingCollection(vm.RootTreeItems, item);
        if (col != null) return (col, idx);
        return FindContainingCollection(vm.FavouriteNotes, item);
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
