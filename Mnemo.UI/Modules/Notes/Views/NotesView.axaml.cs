using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.History;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Components.Overlays;
using Mnemo.UI.Components.BlockEditor;
using Mnemo.UI.Services;
using Mnemo.UI.Controls;
using Mnemo.UI.Modules.Notes.ViewModels;

namespace Mnemo.UI.Modules.Notes.Views;

public partial class NotesView : UserControl
{
    private bool _editorOpenNoteWired;
    private bool _blocksChangedSubscribed;
    private DispatcherTimer? _saveDebounceTimer;
    /// <summary>Note we have pending unsaved block changes for. When flushing on note switch, SelectedNote is already the new note; we must save this one with editor content.</summary>
    private Note? _pendingSaveNote;
    private const int SaveDebounceMs = 500;

    internal DragCoordinator? _dragCoordinator;
    private Window? _attachedWindow;

    private bool _editorZoomHandlersWired;
    private bool _editorScrollPanHandlersWired;
    private bool _editorScrollPanning;
    private Point _editorPanLastPosition;
    private IPointer? _editorPanPointer;
    private double _editorZoom = 1.0;
    private Size _lastEditorDocumentSizeForZoom;
    private const double EditorMinZoom = 0.5;
    private const double EditorMaxZoom = 10.0;

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
        if (e.PropertyName == nameof(NotesViewModel.EditorMaxWidth))
        {
            _lastEditorDocumentSizeForZoom = default;
            SyncEditorDocumentLayoutWidth();
            ApplyEditorCameraZoom();
            Dispatcher.UIThread.Post(() =>
            {
                SyncEditorDocumentLayoutWidth();
                ApplyEditorCameraZoom();
                ResetEditorHorizontalOffset();
            }, DispatcherPriority.Loaded);
            return;
        }

        if (e.PropertyName != nameof(NotesViewModel.SelectedNote))
            return;

        ResetEditorView();
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
            editor.ImageAssetService ??= sp.GetService<IImageAssetService>();
        }

        editor.HostNoteId = vm.SelectedNote?.NoteId;
        editor.NoteTitleResolver = id => vm.ResolveNoteTitleForPageBlock(id);
        editor.ChildPageCountResolver = id => vm.CountDirectChildPagesForNote(id);
        editor.CreateChildPageUnderNoteAsync = vm.CreateChildPageNoteUnderParentAsync;
        var loc = sp?.GetService<ILocalizationService>();
        editor.PageBlockMissingTitle = loc?.T("PageMissingTitle", "NotesEditor") ?? "Missing note";

        if (!_editorOpenNoteWired)
        {
            editor.OpenReferencedNote += OnBlockEditorOpenReferencedNote;
            _editorOpenNoteWired = true;
        }

        editor.FlushPendingNoteSaveAsync = FlushEditorToSelectedNoteAsync;

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
        SetupEditorScrollPanAndGutter();
        SetupEditorCameraZoom();

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
        if (e.Key == Key.Escape && _editorScrollPanning)
        {
            EndEditorScrollPanIfNeeded();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _dragCoordinator?.IsDragging == true)
        {
            _dragCoordinator.CancelDrag();
            e.Handled = true;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _dragCoordinator?.CancelDrag();
        EndEditorScrollPanIfNeeded();
    }

    private void SetupEditorScrollPanAndGutter()
    {
        if (_editorScrollPanHandlersWired) return;
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll == null) return;
        scroll.AddHandler(PointerPressedEvent, OnEditorScrollViewerPointerPressed, RoutingStrategies.Tunnel);
        scroll.PointerMoved += OnEditorScrollViewerPointerMoved;
        scroll.PointerReleased += OnEditorScrollViewerPointerReleased;
        scroll.PointerCaptureLost += OnEditorScrollViewerPointerCaptureLost;
        _editorScrollPanHandlersWired = true;
    }

    private void TeardownEditorScrollPanAndGutter()
    {
        if (!_editorScrollPanHandlersWired) return;
        EndEditorScrollPanIfNeeded();
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll != null)
        {
            scroll.RemoveHandler(PointerPressedEvent, OnEditorScrollViewerPointerPressed);
            scroll.PointerMoved -= OnEditorScrollViewerPointerMoved;
            scroll.PointerReleased -= OnEditorScrollViewerPointerReleased;
            scroll.PointerCaptureLost -= OnEditorScrollViewerPointerCaptureLost;
        }
        _editorScrollPanHandlersWired = false;
    }

    private void OnEditorScrollViewerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll == null) return;

        var pt = e.GetCurrentPoint(scroll);
        if (pt.Properties.IsMiddleButtonPressed)
        {
            TryBeginEditorScrollPan(scroll, e);
            return;
        }

        if (pt.Properties.IsLeftButtonPressed && (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            TryBeginEditorScrollPan(scroll, e);
            return;
        }

        OnGutterPointerPressed(sender, e);
    }

    private void TryBeginEditorScrollPan(ScrollViewer scroll, PointerPressedEventArgs e)
    {
        _editorScrollPanning = true;
        _editorPanPointer = e.Pointer;
        _editorPanLastPosition = e.GetPosition(scroll);
        e.Pointer.Capture(scroll);
        scroll.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void OnEditorScrollViewerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_editorScrollPanning) return;
        var scroll = sender as ScrollViewer ?? this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll == null || !ReferenceEquals(e.Pointer.Captured, scroll)) return;

        var pos = e.GetPosition(scroll);
        var d = pos - _editorPanLastPosition;
        _editorPanLastPosition = pos;
        scroll.Offset -= new Vector(d.X, d.Y);
        ClampEditorScrollOffset();
    }

    private void OnEditorScrollViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var scroll = sender as ScrollViewer ?? this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll == null || !_editorScrollPanning) return;
        if (!ReferenceEquals(e.Pointer.Captured, scroll)) return;

        _editorScrollPanning = false;
        _editorPanPointer = null;
        e.Pointer.Capture(null);
        scroll.Cursor = Cursor.Default;
    }

    private void OnEditorScrollViewerPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndEditorScrollPanIfNeeded();
    }

    private void EndEditorScrollPanIfNeeded()
    {
        if (!_editorScrollPanning && _editorPanPointer == null) return;
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        _editorScrollPanning = false;
        _editorPanPointer?.Capture(null);
        _editorPanPointer = null;
        if (scroll != null)
            scroll.Cursor = Cursor.Default;
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

    /// <summary>100% zoom, scroll to origin, end pan gesture; extent resyncs on next layout.</summary>
    public void ResetEditorView()
    {
        EndEditorScrollPanIfNeeded();
        _editorZoom = 1.0;
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll != null)
            scroll.Offset = default;
        _lastEditorDocumentSizeForZoom = default;
        ApplyEditorCameraZoom();
    }

    private void SetupEditorCameraZoom()
    {
        if (_editorZoomHandlersWired) return;
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        var doc = this.FindControl<StackPanel>("EditorDocumentPanel");
        if (scroll == null || doc == null) return;
        scroll.AddHandler(PointerWheelChangedEvent, OnEditorScrollViewerPointerWheelChanged, RoutingStrategies.Tunnel);
        scroll.ScrollChanged += OnEditorScrollViewerScrollChanged;
        doc.LayoutUpdated += OnEditorDocumentLayoutUpdated;
        _editorZoomHandlersWired = true;
        Dispatcher.UIThread.Post(ApplyEditorCameraZoom, DispatcherPriority.Loaded);
    }

    private void TeardownEditorCameraZoom()
    {
        if (!_editorZoomHandlersWired) return;
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        var doc = this.FindControl<StackPanel>("EditorDocumentPanel");
        scroll?.RemoveHandler(PointerWheelChangedEvent, OnEditorScrollViewerPointerWheelChanged);
        if (scroll != null)
            scroll.ScrollChanged -= OnEditorScrollViewerScrollChanged;
        if (doc != null)
            doc.LayoutUpdated -= OnEditorDocumentLayoutUpdated;
        _editorZoomHandlersWired = false;
    }

    private void OnEditorDocumentLayoutUpdated(object? sender, EventArgs e)
    {
        SyncEditorDocumentLayoutWidth();
        var doc = this.FindControl<StackPanel>("EditorDocumentPanel");
        if (doc == null) return;
        var s = GetEditorDocumentNaturalSize(doc);
        if (s.Width <= 1 || s.Height <= 1) return;
        if (NearlySameSize(s, _lastEditorDocumentSizeForZoom)) return;
        _lastEditorDocumentSizeForZoom = s;
        ApplyEditorCameraZoom();
    }

    private static bool NearlySameSize(Size a, Size b) =>
        Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;

    private void OnEditorScrollViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var mods = e.KeyModifiers;
        if ((mods & KeyModifiers.Control) == 0 && (mods & KeyModifiers.Meta) == 0)
            return;

        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        var outer = this.FindControl<Border>("EditorScrollWidthHost");
        if (scroll == null || outer == null) return;

        double z = _editorZoom;
        var zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
        double newZ = Math.Clamp(z * zoomFactor, EditorMinZoom, EditorMaxZoom);
        if (Math.Abs(newZ - z) < 1e-6)
        {
            e.Handled = true;
            return;
        }

        var viewport = GetEditorViewportSize(scroll);
        var cursorInViewport = e.GetPosition(scroll);
        cursorInViewport = new Point(
            Math.Clamp(cursorInViewport.X, 0, viewport.Width),
            Math.Clamp(cursorInViewport.Y, 0, viewport.Height));
        var oldExtent = GetEditorZoomedExtentSize(z);
        var oldOuterWidth = outer.Width > 1 ? outer.Width : Math.Max(oldExtent.Width, viewport.Width);
        var oldHostX = Math.Max(0, (oldOuterWidth - oldExtent.Width) / 2);
        var contentPoint = new Point(
            scroll.Offset.X + cursorInViewport.X,
            scroll.Offset.Y + cursorInViewport.Y);
        var docPoint = new Point(
            (contentPoint.X - oldHostX) / z,
            contentPoint.Y / z);

        _editorZoom = newZ;
        ApplyEditorCameraZoom();

        var newExtent = GetEditorZoomedExtentSize(newZ);
        var newOuterWidth = outer.Width > 1 ? outer.Width : Math.Max(newExtent.Width, viewport.Width);
        var newHostX = Math.Max(0, (newOuterWidth - newExtent.Width) / 2);
        scroll.Offset = new Vector(
            newHostX + docPoint.X * newZ - cursorInViewport.X,
            docPoint.Y * newZ - cursorInViewport.Y);
        ClampEditorScrollOffset();
        e.Handled = true;
    }

    /// <summary>Desired document size for zoom extent; render transforms do not change layout bounds.</summary>
    private Size GetEditorDocumentNaturalSize(StackPanel doc)
    {
        Size s;
        var d = doc.DesiredSize;
        if (d.Width > 1 && d.Height > 1)
            s = d;
        else
        {
            var b = doc.Bounds.Size;
            if (b.Width <= 1 || b.Height <= 1)
                return b;
            s = b;
        }

        if (DataContext is NotesViewModel vm && vm.EditorMaxWidth > 1)
        {
            var layoutWidth = !double.IsNaN(doc.Width) && doc.Width > 1 ? doc.Width : vm.EditorMaxWidth;
            var minW = layoutWidth + doc.Margin.Left + doc.Margin.Right;
            if (s.Width < minW - 0.5)
                s = new Size(minW, s.Height);
        }

        return s;
    }

    private void ApplyEditorCameraZoom()
    {
        var host = this.FindControl<EditorZoomHost>("EditorZoomExtentHost");
        var doc = this.FindControl<StackPanel>("EditorDocumentPanel");
        if (host == null || doc == null) return;
        SyncEditorDocumentLayoutWidth();
        var s = GetEditorDocumentNaturalSize(doc);
        if (s.Width <= 1 || s.Height <= 1)
        {
            ClearEditorScrollWidthHostSizing();
            return;
        }

        double z = _editorZoom;
        host.Zoom = z;
        host.LayoutWidth = s.Width;
        SyncEditorScrollWidthHost();
        Dispatcher.UIThread.Post(ClampEditorScrollOffset, DispatcherPriority.Loaded);
    }

    private void OnEditorScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        SyncEditorDocumentLayoutWidth();
        _lastEditorDocumentSizeForZoom = default;
        ApplyEditorCameraZoom();
        SyncEditorScrollWidthHost();
        Dispatcher.UIThread.Post(ClampEditorScrollOffset, DispatcherPriority.Loaded);
    }

    private void OnEditorScrollViewerLayoutUpdated(object? sender, EventArgs e)
    {
        if (SyncEditorDocumentLayoutWidth())
        {
            _lastEditorDocumentSizeForZoom = default;
            ApplyEditorCameraZoom();
        }
        else
        {
            SyncEditorScrollWidthHost();
        }
    }

    private void OnEditorScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta == default && e.ViewportDelta == default)
            return;
        Dispatcher.UIThread.Post(ClampEditorScrollOffset, DispatcherPriority.Loaded);
    }

    /// <summary>Scrollable width at least viewport so the zoom host can stay centered when the column is narrow.</summary>
    private void SyncEditorScrollWidthHost()
    {
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        var outer = this.FindControl<Border>("EditorScrollWidthHost");
        if (scroll == null || outer == null) return;

        var extent = GetEditorZoomedExtentSize(_editorZoom);
        var cw = extent.Width;
        var ch = extent.Height;
        if (cw <= 1 || ch <= 1 || double.IsNaN(cw) || double.IsNaN(ch))
        {
            ClearEditorScrollWidthHostSizing();
            return;
        }

        var vw = scroll.Viewport.Width;
        if (vw <= 1)
            vw = scroll.Bounds.Width;
        var targetW = vw > 1 ? Math.Max(cw, vw) : cw;
        outer.Width = targetW;
        outer.Height = ch;
    }

    private Size GetEditorZoomedExtentSize(double zoom)
    {
        var doc = this.FindControl<StackPanel>("EditorDocumentPanel");
        if (doc == null)
            return default;

        var s = GetEditorDocumentNaturalSize(doc);
        return new Size(s.Width * zoom, s.Height * zoom);
    }

    private static Size GetEditorViewportSize(ScrollViewer scroll)
    {
        var viewport = scroll.Viewport;
        var width = viewport.Width > 1 ? viewport.Width : scroll.Bounds.Width;
        var height = viewport.Height > 1 ? viewport.Height : scroll.Bounds.Height;
        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>Editor.Width is a max column width; shrink to the viewport before zoom so normal view never starts horizontally clipped.</summary>
    private bool SyncEditorDocumentLayoutWidth()
    {
        if (DataContext is not NotesViewModel vm || vm.EditorMaxWidth <= 1)
            return false;

        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        var doc = this.FindControl<StackPanel>("EditorDocumentPanel");
        var host = this.FindControl<EditorZoomHost>("EditorZoomExtentHost");
        if (scroll == null || doc == null || host == null)
            return false;

        var vw = GetEditorViewportSize(scroll).Width;
        if (vw <= 1)
            return false;

        var available = Math.Max(1, vw - doc.Margin.Left - doc.Margin.Right);
        var width = Math.Min(vm.EditorMaxWidth, available);
        host.LayoutWidth = width + doc.Margin.Left + doc.Margin.Right;
        if (Math.Abs(doc.Width - width) <= 0.5)
            return false;

        doc.Width = width;
        return true;
    }

    private void ResetEditorHorizontalOffset()
    {
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll == null) return;
        if (Math.Abs(scroll.Offset.X) > 0.01)
            scroll.Offset = new Vector(0, scroll.Offset.Y);
    }

    private void ClearEditorScrollWidthHostSizing()
    {
        var outer = this.FindControl<Border>("EditorScrollWidthHost");
        if (outer == null) return;
        outer.ClearValue(Control.WidthProperty);
        outer.ClearValue(Control.HeightProperty);
    }

    /// <summary>After viewport/extent shrink (resize, zoom), drop stale Offset so content is not clipped on the top/left.</summary>
    private void ClampEditorScrollOffset()
    {
        var scroll = this.FindControl<ScrollViewer>("EditorScrollViewer");
        if (scroll == null) return;

        var extent = scroll.Extent;
        var viewport = scroll.Viewport;
        if (viewport.Width <= 1 || viewport.Height <= 1 || extent.Width <= 1 || extent.Height <= 1)
            return;

        var maxX = Math.Max(0, extent.Width - viewport.Width);
        var maxY = Math.Max(0, extent.Height - viewport.Height);
        var o = scroll.Offset;
        var x = Math.Clamp(o.X, 0, maxX);
        var y = Math.Clamp(o.Y, 0, maxY);
        if (Math.Abs(x - o.X) > 0.01 || Math.Abs(y - o.Y) > 0.01)
            scroll.Offset = new Vector(x, y);
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

        TeardownEditorScrollPanAndGutter();
        TeardownEditorCameraZoom();

        RemoveHandler(PointerMovedEvent, OnPanePointerMoved);
        RemoveHandler(PointerReleasedEvent, OnPanePointerReleased);

        if (DataContext is NotesViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;

        var editor = this.FindControl<BlockEditor>("NoteBlockEditor");
        if (editor != null)
        {
            if (_editorOpenNoteWired)
            {
                editor.OpenReferencedNote -= OnBlockEditorOpenReferencedNote;
                _editorOpenNoteWired = false;
            }
            if (_blocksChangedSubscribed)
            {
                editor.BlocksChanged -= OnBlockEditorBlocksChanged;
                _blocksChangedSubscribed = false;
            }
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnBlockEditorOpenReferencedNote(string noteId)
    {
        if (DataContext is NotesViewModel vm)
            vm.NavigateToNoteById(noteId);
    }

    private async Task FlushEditorToSelectedNoteAsync()
    {
        if (DataContext is not NotesViewModel vm || vm.SelectedNote == null)
            return;
        var editor = this.FindControl<BlockEditor>("NoteBlockEditor");
        if (editor == null)
            return;
        await vm.SaveNoteWithContentAsync(vm.SelectedNote, editor.GetBlocks(), null).ConfigureAwait(true);
    }

    private async void OnTitleBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NotesViewModel vm || vm.SelectedNote == null)
            return;

        var titleBox = sender as TextBox;
        if (titleBox != null)
            await vm.SaveCurrentNoteAsync(null, titleBox.Text);
    }

    private async void OnTransferClick(object? sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        var services = app?.Services;
        var vm = DataContext as NotesViewModel;
        if (services == null || vm == null)
            return;

        var coordinator = services.GetService<IImportExportCoordinator>();
        var overlayService = services.GetService<IOverlayService>();
        var localization = services.GetService<ILocalizationService>();
        if (coordinator == null || overlayService == null)
            return;

        var capabilities = coordinator.GetCapabilities("notes");
        var overlay = new TransferOverlay();
        var canExportSelectedNote = vm.SelectedNote != null;
        overlay.IsExportAvailable = canExportSelectedNote;
        overlay.SetLocalizedChrome(
            "TransferOverlayTitle", "Notes",
            "TransferOverlayDescription", "Notes",
            "Continue", "Common",
            "Cancel", "Common");
        overlay.Initialize(capabilities, defaultImport: !canExportSelectedNote);

        var overlayId = overlayService.CreateOverlay(overlay, new OverlayOptions
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = true
        }, "TransferOverlay");

        var tcs = new TaskCompletionSource<TransferOverlayResult?>();
        overlay.OnResult = result =>
        {
            overlayService.CloseOverlay(overlayId);
            tcs.TrySetResult(result);
        };

        var selected = await tcs.Task.ConfigureAwait(true);
        if (selected == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        if (selected.IsImport)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Import notes",
                FileTypeFilter = [new FilePickerFileType(selected.Format.DisplayName) { Patterns = selected.Format.Extensions.Select(e => $"*{e}").ToArray() }]
            });
            var file = files.FirstOrDefault();
            if (file == null)
                return;

            var preview = await coordinator.PreviewImportAsync(new ImportExportRequest
            {
                ContentType = "notes",
                FormatId = selected.Format.FormatId,
                FilePath = file.Path.LocalPath
            }).ConfigureAwait(true);
            if (!preview.IsSuccess || preview.Value == null)
            {
                await overlayService.CreateDialogAsync("Import failed", preview.ErrorMessage ?? "Could not preview file.").ConfigureAwait(true);
                return;
            }

            var summary = string.Join(", ", preview.Value.DiscoveredCounts.Select(p => $"{p.Value} {p.Key}"));
            var confirm = await overlayService.CreateDialogAsync("Confirm Import", $"This file contains: {summary}", "Import", "Cancel").ConfigureAwait(true);
            if (!string.Equals(confirm, "Import", StringComparison.Ordinal))
                return;

            var result = await coordinator.ImportAsync(new ImportExportRequest
            {
                ContentType = "notes",
                FormatId = selected.Format.FormatId,
                FilePath = file.Path.LocalPath,
                Options = new Dictionary<string, object?>
                {
                    ["DuplicateOnConflict"] = selected.DuplicateOnConflict,
                    ["StrictUnknownPayloads"] = selected.StrictUnknownPayloads
                }
            }).ConfigureAwait(true);

            await overlayService.CreateDialogAsync(result.IsSuccess ? "Import complete" : "Import failed",
                result.IsSuccess ? "Notes import finished." : result.ErrorMessage ?? "Import failed.").ConfigureAwait(true);
            if (result.IsSuccess)
                await vm.LoadNotesCommand.ExecuteAsync(null);
            return;
        }

        if (vm.SelectedNote == null)
            return;

        await FlushEditorToSelectedNoteAsync().ConfigureAwait(true);

        var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export notes",
            SuggestedFileName = $"notes{selected.Format.Extensions.FirstOrDefault() ?? ".mnemo"}",
            DefaultExtension = selected.Format.Extensions.FirstOrDefault()?.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(selected.Format.DisplayName) { Patterns = selected.Format.Extensions.Select(ext => $"*{ext}").ToArray() }]
        });
        if (saveFile == null)
            return;

        var exportResult = await coordinator.ExportAsync(new ImportExportRequest
        {
            ContentType = "notes",
            FormatId = selected.Format.FormatId,
            FilePath = saveFile.Path.LocalPath,
            Payload = vm.SelectedNote
        }).ConfigureAwait(true);

        await overlayService.CreateDialogAsync(exportResult.IsSuccess ? "Export complete" : "Export failed",
            exportResult.IsSuccess ? "Notes export finished." : exportResult.ErrorMessage ?? "Export failed.").ConfigureAwait(true);
    }
}
