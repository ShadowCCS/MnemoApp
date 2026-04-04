using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.History;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Components.BlockEditor;
using Mnemo.UI.Services;
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

    internal DragCoordinator? _dragCoordinator;
    private Window? _attachedWindow;

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

        var sp = ((App)Application.Current!).Services;
        if (editor.History == null)
        {
            var historyManager = sp?.GetService<IHistoryManager>();
            if (historyManager != null)
                editor.History = historyManager;
        }
        if (sp != null)
        {
            editor.NoteClipboardCodec ??= sp.GetService<INoteClipboardPayloadCodec>();
            editor.NoteClipboardService ??= sp.GetService<INoteClipboardPlatformService>();
        }

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

        SetupDragCoordinator();
        SetupGutterBoxSelect();

        _attachedWindow = this.GetVisualAncestors().OfType<Window>().FirstOrDefault();
        if (_attachedWindow != null)
        {
            _attachedWindow.KeyDown += OnWindowKeyDown;
            _attachedWindow.Deactivated += OnWindowDeactivated;
        }
    }

    private void SetupDragCoordinator()
    {
        // Register global pointer handlers immediately — these only do work when _dragCoordinator.IsDragging
        AddHandler(PointerMovedEvent, OnPanePointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPanePointerReleased, RoutingStrategies.Tunnel);
    }

    private void EnsureDragCoordinator()
    {
        if (_dragCoordinator != null) return;

        // By the time a drag starts, the visual tree is fully realized — walk it now
        var overlayCanvas = this.GetVisualDescendants().OfType<Canvas>().FirstOrDefault(c => c.Name == "DragOverlayCanvas");
        var scrollViewer = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "SidebarScrollViewer");
        var paneRoot = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "SidebarPaneBorder");

        if (overlayCanvas == null || scrollViewer == null || paneRoot == null) return;

        _dragCoordinator = new DragCoordinator(overlayCanvas, scrollViewer, paneRoot);
    }

    private void OnPanePointerMoved(object? sender, PointerEventArgs e)
    {
        _dragCoordinator?.OnPointerMoved(e);
    }

    private async void OnPanePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragCoordinator == null || !_dragCoordinator.IsDragging) return;
        if (DataContext is not NotesViewModel vm) return;

        var source = _dragCoordinator.SourceItem;
        var drop = _dragCoordinator.OnPointerReleased(e);

        if (source == null) return;

        if (drop == null)
        {
            // Dropped on empty space → move to root
            await vm.MoveTreeItemToRootAsync(source);
            return;
        }

        bool insertAfter = drop.Value.Mode == DragCoordinator.DropMode.InsertBelow;
        bool dropOnFolder = drop.Value.Mode == DragCoordinator.DropMode.DropIntoFolder;
        await vm.MoveTreeItemAsync(source, drop.Value.Target, dropOnFolder, insertAfter);
    }

    /// <summary>Called by <see cref="NoteTreeRow"/> once drag threshold is crossed.</summary>
    public void InitiateDrag(NoteTreeItemViewModel item, NoteTreeRow row, IPointer pointer)
    {
        EnsureDragCoordinator();
        _dragCoordinator?.BeginDrag(item, row, pointer);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragCoordinator?.IsDragging == true)
        {
            _dragCoordinator.CancelDrag();
            e.Handled = true;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _dragCoordinator?.CancelDrag();
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

        var posInEditor = e.GetPosition(editor);
        var editorBounds = new Rect(0, 0, editor.Bounds.Width, editor.Bounds.Height);
        if (editorBounds.Contains(posInEditor)) return;

        if (posInEditor.Y < 0 || posInEditor.Y > editor.Bounds.Height) return;

        editor.ArmExternalBoxSelect(posInEditor, e.Pointer);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _dragCoordinator?.Dispose();
        _dragCoordinator = null;

        if (_attachedWindow != null)
        {
            _attachedWindow.KeyDown -= OnWindowKeyDown;
            _attachedWindow.Deactivated -= OnWindowDeactivated;
            _attachedWindow = null;
        }

        FlushPendingSave();

        var titleBox = this.FindControl<TextBox>("NoteTitleBox");
        if (titleBox != null)
            titleBox.LostFocus -= OnTitleBoxLostFocus;

        var editorScrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
        editorScrollViewer?.RemoveHandler(PointerPressedEvent, OnGutterPointerPressed);

        RemoveHandler(PointerMovedEvent, OnPanePointerMoved);
        RemoveHandler(PointerReleasedEvent, OnPanePointerReleased);

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
