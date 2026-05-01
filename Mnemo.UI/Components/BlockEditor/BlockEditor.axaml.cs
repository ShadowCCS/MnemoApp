using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Formatting;
using Mnemo.Core.History;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Common;
using Mnemo.UI.Modules.Notes.Operations;
using Mnemo.UI.Services;

namespace Mnemo.UI.Components.BlockEditor;

public partial class BlockEditor : UserControl, INotifyPropertyChanged
{
    private ObservableCollection<BlockViewModel> _blocks = new();

    /// <summary>Visual rows (one item per stack row; split pairs are one row with two <see cref="EditableBlock"/>s).</summary>
    public ObservableCollection<BlockRowViewModelBase> BlockRows { get; } = new();

    /// <summary>Top-level <see cref="Blocks"/> index for the row containing keyboard focus (split row = one index).</summary>
    private int _focusedBlockIndex = -1;

    /// <summary>Leaf block id with keyboard focus (including nested split cells).</summary>
    private string? _focusedBlockId;

    // Drag-box block selection (Mode 2)
    private bool _isBoxSelecting;
    private bool _boxSelectArmed;   // true after pointer-down outside blocks, waiting for threshold
    private Point _boxSelectStart;   // editor space (for hit-test)
    private Point _boxSelectStartInOverlay; // overlay space (for drawing); set when arming
    private Border? _selectionBoxBorder;
    private const double BoxSelectThreshold = 6.0; // pixels to move before box appears
    /// <summary>Anchor for Shift+click range on drag handles; reset when block selection is cleared.</summary>
    private int _blockDragHandleSelectionAnchorIndex = -1;
    private bool _isColumnSplitResizing;
    /// <summary>Horizontal padding on EditorRoot; selection box Margin is relative to the padded content area.</summary>
    private const double EditorContentPaddingX = 32.0;

    private LayoutOverlayPanel? _blockDragGhostOverlay;
    private Border? _blockDragGhostBorder;
    private Vector _blockDragGhostPointerOffset;
    private const double BlockDragGhostOpacity = 0.45;
    private Border? _externalImageDragGhostBorder;
    private static readonly HttpClient ExternalImageHttpClient = new();

    /// <summary>Notes editor: scroll <see cref="ScrollViewer"/> while reordering blocks near viewport top/bottom (same idea as sidebar <see cref="Mnemo.UI.Modules.Notes.Views.DragCoordinator"/>).</summary>
    private DispatcherTimer? _blockDragAutoScrollTimer;
    private double _blockDragAutoScrollDirection;
    private const double BlockDragAutoScrollZone = 40.0;
    private const double BlockDragAutoScrollStep = 9.0;
    private const int BlockDragAutoScrollIntervalMs = 50;
    private readonly record struct FindMatch(string BlockId, int Start, int Length);
    private readonly List<FindMatch> _findMatches = new();
    private int _activeFindMatchIndex = -1;
    private string _findQuery = string.Empty;
    private string _replaceQuery = string.Empty;
    private bool _findPanelVisible;
    private bool _replacePanelExpanded;
    private IOverlayService? _overlayService;
    private string? _findOverlayId;
    private NoteFindPanel? _findPanel;
    private ScrollViewer? _findAnchorScrollHost;
    private EventHandler<SizeChangedEventArgs>? _findAnchorScrollSizeChangedHandler;
    private double? _lastFindOverlayAnchorX;
    private double? _lastFindOverlayAnchorY;
    private bool _isSyncingFindOptionToggles;
    private CaretState? _findCaretBeforeOpen;
    private bool _findNavigatedToMatch;

    /// <summary>Fixed width used for top-right anchor math so overlay position does not feed back into layout.</summary>
    private const double FindOverlayAnchorWidthEstimate = 332.0;

    // Cross-block text selection (Mode 1)
    private bool _isCrossBlockSelecting;
    private bool _crossBlockArmed;  // true after pointer-down inside TextBox, waiting for first move outside
    /// <summary>True while a cross-block text selection drag is actively in progress. Used by EditableBlock to suppress focus side-effects during the drag.</summary>
    public bool IsCrossBlockSelectingActive => _isCrossBlockSelecting;
    private BlockViewModel? _crossBlockAnchorBlock;
    private int _crossBlockAnchorBlockIndex = -1;
    private int _crossBlockAnchorCharIndex;
    private Point _crossBlockStartPoint;
    /// <summary>Hysteresis: last endpoint block index to avoid boundary flicker when dragging across blocks.</summary>
    private int _lastCrossBlockCurrentIndex = -1;
    /// <summary>True while applying inline format to a cross-block selection; prevents focus changes from clearing other blocks' selection.</summary>
    private bool _isApplyingCrossBlockFormat;

    // History / undo-redo
    private IHistoryManager? _history;
    private BlockSnapshot[]? _pendingSnapshot;
    private CaretState? _pendingCaretBefore;
    private bool _isRestoringFromHistory;

    // Text-edit batching (300ms idle flush)
    private DispatcherTimer? _typingBatchTimer;
    private string? _typingBatchBlockId;
    private List<InlineSpan>? _typingBatchRunsBefore;
    private CaretState? _typingBatchCaretBefore;
    private const int TypingBatchIdleMs = 300;

    /// <summary>
    /// Set by the owning view to enable document-wide undo/redo.
    /// Cleared on document switch so history does not leak between notes.
    /// </summary>
    public IHistoryManager? History
    {
        get => _history;
        set => _history = value;
    }

    /// <summary>Optional: set from the host view for Mnemo JSON + markdown clipboard.</summary>
    public INoteClipboardPayloadCodec? NoteClipboardCodec { get; set; }

    /// <summary>Optional: set from the host view for multi-format clipboard I/O.</summary>
    public INoteClipboardPlatformService? NoteClipboardService { get; set; }

    /// <summary>Optional: image import when pasting paths / duplicating / hydrating clipboard blocks.</summary>
    public IImageAssetService? ImageAssetService { get; set; }
    private static readonly TimeSpan OrphanImageDeleteDelay = TimeSpan.FromSeconds(2);
    private static readonly object PendingOrphanImageDeletesGate = new();
    private static readonly Dictionary<string, CancellationTokenSource> PendingOrphanImageDeletes = new(StringComparer.OrdinalIgnoreCase);
    private const string NativeTwoColumnMetaKey = "nativeTwoColumn";

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<BlockEditor, bool>(nameof(IsReadOnly), defaultValue: false);

    /// <summary>
    /// When true, block-structural interactions are disabled (drag/drop, slash menu, block add/delete/reorder).
    /// Rich text selection stays available for copy scenarios.
    /// </summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public ObservableCollection<BlockViewModel> Blocks
    {
        get => _blocks;
        set
        {
            if (ReferenceEquals(_blocks, value)) return;
            _blocks.CollectionChanged -= OnBlocksCollectionChanged;
            _blocks = value ?? new ObservableCollection<BlockViewModel>();
            _blocks.CollectionChanged += OnBlocksCollectionChanged;
            OnPropertyChanged();
            RebuildBlockRows();
        }
    }

    private void OnBlocksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                {
                    var block = (BlockViewModel)e.NewItems[i]!;
                    var index = e.NewStartingIndex + i;
                    BlockRows.Insert(index, MakeRow(block, index));
                    for (var j = index + 1; j < BlockRows.Count; j++)
                        BlockRows[j].StartBlockIndex = j;
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                for (var i = 0; i < e.OldItems!.Count; i++)
                    BlockRows.RemoveAt(e.OldStartingIndex);
                for (var j = e.OldStartingIndex; j < BlockRows.Count; j++)
                    BlockRows[j].StartBlockIndex = j;
                break;

            case NotifyCollectionChangedAction.Move:
                BlockRows.Move(e.OldStartingIndex, e.NewStartingIndex);
                var lo = Math.Min(e.OldStartingIndex, e.NewStartingIndex);
                var hi = Math.Max(e.OldStartingIndex, e.NewStartingIndex);
                for (var j = lo; j <= hi; j++)
                    BlockRows[j].StartBlockIndex = j;
                break;

            case NotifyCollectionChangedAction.Reset:
                RebuildBlockRows();
                break;

            default:
                RebuildBlockRows();
                break;
        }
    }

    private void RebuildBlockRows()
    {
        BlockRows.Clear();
        for (var i = 0; i < Blocks.Count; i++)
            BlockRows.Add(MakeRow(Blocks[i], i));
    }

    private static BlockRowViewModelBase MakeRow(BlockViewModel block, int index) =>
        block is TwoColumnBlockViewModel tc
            ? new SplitBlockRowViewModel(tc, index)
            : new SingleBlockRowViewModel(block, index);

    // TopLevel handlers for global pointer tracking (needed to intercept moves even when TextBox has capture)
    private TopLevel? _topLevel;
    private EventHandler<PointerEventArgs>? _globalPointerMovedHandler;
    private EventHandler<PointerReleasedEventArgs>? _globalPointerReleasedHandler;

    public BlockEditor()
    {
        InitializeComponent();
        _blocks.CollectionChanged += OnBlocksCollectionChanged;
        DataContext = this;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Editor_DragOver_BlockGhost, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, Editor_Drop, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, Editor_DragLeave, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, Editor_PointerPressedTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, Editor_PointerPressedBubble, RoutingStrategies.Bubble, handledEventsToo: true);
        // When we capture the pointer (cross-block or box-select), we receive moves/releases on this control
        AddHandler(PointerMovedEvent, Editor_PointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Editor_PointerReleased, RoutingStrategies.Tunnel);
        // Tunnel so we get keys before the focused TextBox (for block-selection Backspace/Copy/Paste)
        AddHandler(KeyDownEvent, Editor_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, Editor_KeyDown_Bubble, RoutingStrategies.Bubble);
        Loaded += Editor_Loaded;
        Unloaded += Editor_Unloaded;
        _overlayService = ((App?)Application.Current)?.Services?.GetService<IOverlayService>();

        // Don't add initial block here - let LoadBlocks handle it
    }

    private void Editor_Loaded(object? sender, RoutedEventArgs e)
    {
        ResolveSelectionBoxBorder();

        // Register global pointer handlers on TopLevel so we receive moves/releases
        // even when a child TextBox has captured the pointer for its own text selection.
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
        {
            _globalPointerMovedHandler = Editor_PointerMoved;
            _globalPointerReleasedHandler = Editor_PointerReleased;
            _topLevel.AddHandler(PointerMovedEvent, _globalPointerMovedHandler, RoutingStrategies.Tunnel);
            _topLevel.AddHandler(PointerReleasedEvent, _globalPointerReleasedHandler, RoutingStrategies.Tunnel);
        }
    }

    private void Editor_Unloaded(object? sender, RoutedEventArgs e)
    {
        CloseFindPanel(clearQuery: false);

        if (_topLevel != null)
        {
            if (_globalPointerMovedHandler != null)
                _topLevel.RemoveHandler(PointerMovedEvent, _globalPointerMovedHandler);
            if (_globalPointerReleasedHandler != null)
                _topLevel.RemoveHandler(PointerReleasedEvent, _globalPointerReleasedHandler);
            _globalPointerMovedHandler = null;
            _globalPointerReleasedHandler = null;
            _topLevel = null;
        }

        EndBlockDragGhost();
    }

    private void ResolveSelectionBoxBorder()
    {
        _selectionBoxBorder ??= this.FindControl<Border>("SelectionBoxBorder");
        if (_selectionBoxBorder == null)
        {
            var grid = this.FindControl<Grid>("EditorRoot");
            _selectionBoxBorder = grid?.GetVisualChildren().OfType<Border>().FirstOrDefault(c => c.Name == "SelectionBoxBorder");
        }
    }

    /// <summary>
    /// During <see cref="DragDrop.DoDragDropAsync"/>, pointer move events are not delivered; <see cref="DragDrop.DragOverEvent"/> is.
    /// </summary>
    private void Editor_DragOver_BlockGhost(object? sender, DragEventArgs e)
    {
        bool isBlockReorderDrag =
            e.DataTransfer.Contains(BlockViewModel.BlockDragDataFormat)
            || e.DataTransfer.Contains(BlockViewModel.BlockReorderDragPayload.Format);
        if (!isBlockReorderDrag)
        {
            if (TryHandleExternalImageDragOver(e))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            HideExternalImageDragGhost();
            e.DragEffects = DragDropEffects.None;
            return;
        }

        HideExternalImageDragGhost();

        if (!TryResolveBlockReorderPayload(e, out var payload) || payload == null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var pos = e.GetPosition(this);
        if (_blockDragGhostBorder != null && _blockDragGhostOverlay != null)
            UpdateBlockDragGhostFromEditorPoint(pos);

        e.DragEffects = DragDropEffects.Move;
        HandleBlockDragOver(pos, payload);
    }

    private static bool TryResolveBlockReorderPayload(DragEventArgs e, out BlockViewModel.BlockReorderDragPayload? payload)
    {
        if (e.DataTransfer.TryGetValue(BlockViewModel.BlockReorderDragPayload.Format) is BlockViewModel.BlockReorderDragPayload p)
        {
            payload = p;
            return true;
        }

        if (e.DataTransfer.TryGetValue(BlockViewModel.BlockDragDataFormat) is not { } vm)
        {
            payload = null;
            return false;
        }

        payload = new BlockViewModel.BlockReorderDragPayload
        {
            Primary = vm,
            BlocksInDocumentOrder = new[] { vm }
        };
        return true;
    }

    private async void Editor_Drop(object? sender, DragEventArgs e)
    {
        if (e.Handled) return;

        if (!TryResolveBlockReorderPayload(e, out var payload) || payload == null)
        {
            if (await TryDropExternalImageAsync(e).ConfigureAwait(true))
                e.Handled = true;
            return;
        }

        try
        {
            TryPerformDrop(payload);
            e.Handled = true;
        }
        catch (Exception)
        {
        }
        finally
        {
            ClearDropIndicator();
        }
    }

    private void Editor_DragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave bubbles on every child-to-child transition inside the editor.
        // Only clear the indicator when the cursor has actually left the editor bounds.
        var pos = e.GetPosition(this);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (!bounds.Contains(pos))
        {
            ClearDropIndicator();
            HideExternalImageDragGhost();
        }
    }

    private bool TryHandleExternalImageDragOver(DragEventArgs e)
    {
        if (!IsExternalImageDragData(e.DataTransfer))
            return false;

        var cursor = e.GetPosition(this);
        ShowExternalImageDragGhost(cursor);
        if (TryGetUnassignedImageBlockAtPoint(cursor) != null)
        {
            ClearDropIndicator();
            return true;
        }

        var probeImage = BlockFactory.CreateBlock(BlockType.Image, 0);
        if (TryGetColumnDropInsert(cursor, probeImage, out var tc, out var leftColumn, out var insertIndex))
        {
            if (!(ReferenceEquals(_columnDropTarget, tc)
                && _columnDropLeft == leftColumn
                && _columnDropInsertIndex == insertIndex
                && this.FindControl<Border>("BlockReorderDropLineOverlay") is { IsVisible: true }))
            {
                ClearDropIndicator();
                _columnDropTarget = tc;
                _columnDropLeft = leftColumn;
                _columnDropInsertIndex = insertIndex;
                _currentDropInsertIndex = -1;
                ShowColumnDropLineInOverlay(tc, leftColumn, insertIndex);
            }

            return true;
        }

        var topInsert = GetInsertIndex(cursor.Y);
        if (topInsert < 0)
            return false;

        if (_currentDropInsertIndex != topInsert || _columnDropTarget != null)
        {
            ClearDropIndicator();
            _currentDropInsertIndex = topInsert;
            ShowHorizontalReorderDropLineInOverlay(topInsert);
        }

        return true;
    }

    private async Task<bool> TryDropExternalImageAsync(DragEventArgs e)
    {
        if (!IsExternalImageDragData(e.DataTransfer))
            return false;

        if (e.DataTransfer is not IAsyncDataTransfer asyncTransfer)
        {
            return false;
        }

        var droppedImage = await TryCreateImageBlockFromDropDataAsync(asyncTransfer).ConfigureAwait(true);
        if (droppedImage == null)
            return false;

        var cursor = e.GetPosition(this);
        try
        {
            BeginStructuralChange();

            var unassignedTarget = TryGetUnassignedImageBlockAtPoint(cursor);
            if (unassignedTarget != null)
            {
                var importedPath = await ImportImagePathForTargetAsync(droppedImage.ImagePath, unassignedTarget.Id).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(importedPath))
                    unassignedTarget.ImagePath = importedPath;
                else
                    unassignedTarget.ImagePath = droppedImage.ImagePath;

                unassignedTarget.ImageWidth = 0;
                unassignedTarget.SetSpans(new List<InlineSpan> { InlineSpan.Plain(string.Empty) });
                ClearBlockSelection();
                CommitStructuralChange("Drop image");
                BlocksChanged?.Invoke();
                Dispatcher.UIThread.Post(() => unassignedTarget.IsFocused = true, DispatcherPriority.Input);
                return true;
            }

            await HydratePastedImageBlocksAsync(new BlockViewModel[] { droppedImage }).ConfigureAwait(true);

            if (_columnDropTarget != null && _columnDropInsertIndex >= 0)
            {
                var column = _columnDropLeft ? _columnDropTarget.LeftColumnBlocks : _columnDropTarget.RightColumnBlocks;
                var insertAt = Math.Clamp(_columnDropInsertIndex, 0, column.Count);
                BlockHierarchy.WireChildOwnership(_columnDropTarget, droppedImage, _columnDropLeft);
                SubscribeToBlock(droppedImage);
                column.Insert(insertAt, droppedImage);
            }
            else
            {
                var insertIndex = _currentDropInsertIndex;
                if (insertIndex < 0 || insertIndex > Blocks.Count)
                {
                    insertIndex = GetInsertIndex(cursor.Y);
                    if (insertIndex < 0)
                        insertIndex = Blocks.Count;
                }

                insertIndex = Math.Clamp(insertIndex, 0, Blocks.Count);
                SubscribeToBlock(droppedImage);
                Blocks.Insert(insertIndex, droppedImage);
                droppedImage.Order = insertIndex;
            }

            ReorderBlocks();
            ClearBlockSelection();
            CommitStructuralChange("Drop image");
            BlocksChanged?.Invoke();
            Dispatcher.UIThread.Post(() => droppedImage.IsFocused = true, DispatcherPriority.Input);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            ClearDropIndicator();
            HideExternalImageDragGhost();
        }
    }

    private async Task<BlockViewModel?> TryCreateImageBlockFromDropDataAsync(IAsyncDataTransfer data)
    {
        try
        {
            var files = await data.TryGetFilesAsync().ConfigureAwait(true);
            if (files != null)
            {
                foreach (var f in files)
                {
                    var p = f.TryGetLocalPath();
                    if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) continue;
                    if (!ClipboardImageExtensions.Contains(Path.GetExtension(p))) continue;
                    return CreateImageBlockStubForPaste(p);
                }
            }
        }
        catch
        {
            // fall through to bitmap/text probes
        }

        var bitmap = await data.TryGetBitmapAsync().ConfigureAwait(true);
        if (bitmap != null)
        {
            try
            {
                return await SaveClipboardBitmapToNewImageBlockAsync(bitmap).ConfigureAwait(true);
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        if (data is IDataTransfer syncTransfer
            && syncTransfer.TryGetValue(DataFormat.Text) is string text)
        {
            var candidate = NormalizeSingleLineImagePathFromClipboard(text);
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)
                && ClipboardImageExtensions.Contains(Path.GetExtension(candidate)))
            {
                return CreateImageBlockStubForPaste(candidate);
            }

            var url = NormalizeSingleLineImageUrlFromDragText(text);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return await DownloadExternalImageToNewImageBlockAsync(url).ConfigureAwait(true);
            }
        }

        try
        {
            var asyncText = await data.TryGetTextAsync().ConfigureAwait(true);
            var candidate = NormalizeSingleLineImagePathFromClipboard(asyncText);
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)
                && ClipboardImageExtensions.Contains(Path.GetExtension(candidate)))
            {
                return CreateImageBlockStubForPaste(candidate);
            }

            var url = NormalizeSingleLineImageUrlFromDragText(asyncText);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return await DownloadExternalImageToNewImageBlockAsync(url).ConfigureAwait(true);
            }
        }
        catch
        {
            // Some sources expose text only through sync formats; ignore.
        }

        return null;
    }

    private static bool IsExternalImageDragData(IDataTransfer data)
    {
        if (data.Contains(DataFormat.File))
            return true;

        if (data.TryGetValue(DataFormat.Text) is string text)
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return true;
            if (NormalizeSingleLineImagePathFromClipboard(trimmed) != null)
                return true;
        }

        // Browser drags frequently expose URL text only via async transfer APIs.
        return data is IAsyncDataTransfer;
    }

    private BlockViewModel? TryGetUnassignedImageBlockAtPoint(Point cursorPosInEditor)
    {
        foreach (var vm in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            if (vm.Type != BlockType.Image)
                continue;
            if (!string.IsNullOrWhiteSpace(vm.ImagePath))
                continue;

            var bounds = GetEditableBlockBoundsInEditor(GetEditableBlockForViewModel(vm));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;
            if (bounds.Contains(cursorPosInEditor))
                return vm;
        }

        return null;
    }

    private async Task<string?> ImportImagePathForTargetAsync(string? sourcePath, string targetBlockId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return sourcePath;

        var svc = ResolveImageAssetService();
        if (svc == null)
            return sourcePath;

        try
        {
            var imported = await svc.ImportAndCopyAsync(sourcePath, targetBlockId).ConfigureAwait(true);
            if (imported.IsSuccess && !string.IsNullOrWhiteSpace(imported.Value))
                return imported.Value;
        }
        catch
        {
            // Keep original path when import fails (e.g. locked file).
        }

        return sourcePath;
    }

    private void ShowExternalImageDragGhost(Point cursorPosInEditor)
    {
        var overlay = this.FindControl<LayoutOverlayPanel>("BlockDragGhostOverlay");
        if (overlay == null)
            return;

        if (_externalImageDragGhostBorder == null)
        {
            _externalImageDragGhostBorder = new Border
            {
                Width = 132,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(230, 33, 33, 33)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "Drop image",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White,
                    FontSize = 12
                }
            };

            overlay.Children.Add(_externalImageDragGhostBorder);
        }

        Canvas.SetLeft(_externalImageDragGhostBorder, cursorPosInEditor.X + 14);
        Canvas.SetTop(_externalImageDragGhostBorder, cursorPosInEditor.Y + 14);
        _externalImageDragGhostBorder.IsVisible = true;
        overlay.InvalidateArrange();
    }

    private void HideExternalImageDragGhost()
    {
        if (_externalImageDragGhostBorder == null)
            return;
        _externalImageDragGhostBorder.IsVisible = false;
    }

    private static string? NormalizeSingleLineImageUrlFromDragText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var candidate = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("#", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.ToString();
    }

    private static async Task<BlockViewModel?> DownloadExternalImageToNewImageBlockAsync(string imageUrl)
    {
        try
        {
            using var response = await ExternalImageHttpClient.GetAsync(imageUrl).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
                return null;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType)
                && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
            if (bytes.Length == 0)
                return null;

            // Validate formats that Avalonia reliably decodes before persisting.
            if (mediaType is null
                || mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("image/tiff", StringComparison.OrdinalIgnoreCase))
            {
                await using var probe = new MemoryStream(bytes, writable: false);
                try
                {
                    using var _ = new Bitmap(probe);
                }
                catch
                {
                    return null;
                }
            }

            var vm = BlockFactory.CreateBlock(BlockType.Image, 0);
            var dir = MnemoAppPaths.GetImagesDirectory();
            Directory.CreateDirectory(dir);

            var ext = GuessImageFileExtension(response.Content.Headers.ContentType?.MediaType, imageUrl);
            var path = Path.Combine(dir, vm.Id + ext);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);

            vm.ImagePath = path;
            vm.ImageWidth = 0;
            vm.SetSpans(new List<InlineSpan> { InlineSpan.Plain(string.Empty) });
            return vm;
        }
        catch
        {
            return null;
        }
    }

    private static string GuessImageFileExtension(string? mediaType, string sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            if (mediaType.Contains("png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (mediaType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("jpg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
            if (mediaType.Contains("gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
            if (mediaType.Contains("webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
            if (mediaType.Contains("bmp", StringComparison.OrdinalIgnoreCase)) return ".bmp";
            if (mediaType.Contains("tiff", StringComparison.OrdinalIgnoreCase)) return ".tiff";
        }

        var ext = Path.GetExtension(sourceUrl);
        return ClipboardImageExtensions.Contains(ext) ? ext : ".png";
    }

    public void LoadBlocks(Block[] blocks)
    {
        FlushTypingBatch();
        _pendingSnapshot = null;
        _pendingCaretBefore = null;
        _history?.Clear();

        // Unsubscribe from old blocks (do not delete image assets — they may still belong to the note on disk)
        foreach (var block in Blocks)
            UnsubscribeFromBlock(block, removeStoredImageIfOrphan: false);

        // Create new collection to ensure proper UI notification
        var newBlocks = new ObservableCollection<BlockViewModel>();
        
        if (blocks == null || blocks.Length == 0)
        {
            var defaultBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
            SubscribeToBlock(defaultBlock);
            newBlocks.Add(defaultBlock);
        }
        else
        {
            var expanded = ColumnPairHelper.ExpandLegacyTwoColumnBlocks(blocks.Where(b => b != null));
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var block in expanded.OrderBy(b => b.Order))
            {
                if (!string.IsNullOrEmpty(block.Id) && !seenIds.Add(block.Id))
                    continue;

                BlockViewModel viewModel;
                if (ColumnPairHelper.IsNestedTwoColumnBlock(block))
                {
                    var tc = new TwoColumnBlockViewModel(block);
                    SubscribeToBlock(tc);
                    viewModel = tc;
                }
                else
                {
                    viewModel = new BlockViewModel(block);
                    SubscribeToBlock(viewModel);
                }
                newBlocks.Add(viewModel);
            }

            ColumnPairHelper.MergeConsecutiveColumnPairs(newBlocks);
            
            // If no valid blocks were added, add a default one
            if (newBlocks.Count == 0)
            {
                var defaultBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
                SubscribeToBlock(defaultBlock);
                newBlocks.Add(defaultBlock);
            }
        }
        
        // Replace entire collection to trigger UI update
        _focusedBlockIndex = -1;
        _focusedBlockId = null;
        Blocks = newBlocks;
        
        // Update list numbers after loading
        UpdateListNumbers();
        
        // Focus the first block after UI updates to make it immediately editable
        if (newBlocks.Count > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => newBlocks[0].IsFocused = true, 
                Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    public Block[] GetBlocks()
    {
        // Update order before returning
        for (int i = 0; i < Blocks.Count; i++)
        {
            Blocks[i].Order = i;
        }

        return Blocks.Select(b => b.ToBlock()).ToArray();
    }

    public void AddBlock(BlockType type, int? position = null, string? initialContent = null)
    {
        var order = position ?? Blocks.Count;
        var block = BlockFactory.CreateBlock(type, order);
        if (initialContent != null)
            block.Content = initialContent;
        SubscribeToBlock(block);

        if (position.HasValue && position.Value < Blocks.Count)
        {
            Blocks.Insert(position.Value, block);
        }
        else
        {
            Blocks.Add(block);
        }
        ReorderBlocks();
    }

    /// <summary>Replaces a top-level block with a two-column row (empty text in each column). No-op if the block is not in <see cref="Blocks"/> or is inside a column.</summary>
    public void ReplaceBlockWithTwoColumn(BlockViewModel block)
    {
        if (block.OwnerTwoColumn != null) return;

        var index = Blocks.IndexOf(block);
        if (index < 0) return;

        BeginStructuralChange();

        UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
        Blocks.RemoveAt(index);

        var tc = (TwoColumnBlockViewModel)BlockFactory.CreateBlock(BlockType.TwoColumn, block.Order);
        tc.Meta[NativeTwoColumnMetaKey] = true;
        SubscribeToBlock(tc);
        Blocks.Insert(index, tc);
        ReorderBlocks();
        CommitStructuralChange("Two columns");
        NotifyBlocksChanged();

        var left = tc.LeftColumnBlocks[0];
        left.PendingCaretIndex = 0;
        Dispatcher.UIThread.Post(() => left.IsFocused = true, DispatcherPriority.Render);
    }

    /// <summary>Appends an empty text block to a split column (tap below the column stack). Focuses the last cell if it is already empty.</summary>
    internal void TryAddEmptyBlockInSplitColumn(TwoColumnBlockViewModel tc, bool leftColumn)
    {
        if (Blocks.IndexOf(tc) < 0) return;

        var col = leftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
        if (col.Count > 0 && BlockEditorContentPolicy.IsVisuallyEmpty(col[^1].Content))
        {
            var last = col[^1];
            last.PendingCaretIndex = 0;
            Dispatcher.UIThread.Post(() => last.IsFocused = true, DispatcherPriority.Input);
            return;
        }

        BeginStructuralChange();
        var newBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
        col.Add(newBlock);
        ReorderBlocks();
        CommitStructuralChange("New block in column");
        newBlock.PendingCaretIndex = 0;
        Dispatcher.UIThread.Post(() => newBlock.IsFocused = true, DispatcherPriority.Render);
    }

    /// <summary>
    /// Splits a text block at [start, end) into [before][equation][after] blocks.
    /// The selected text becomes the LaTeX source for the new equation block.
    /// </summary>
    public void SplitBlockIntoEquation(BlockViewModel block, int selStart, int selEnd)
    {
        var index = Blocks.IndexOf(block);
        if (index < 0) return;

        var runs = block.Spans;
        var selectedText = Core.Formatting.InlineSpanFormatApplier.Flatten(
            Core.Formatting.InlineSpanFormatApplier.SliceRuns(runs, selStart, selEnd));
        var latex = Core.Formatting.EquationLatexNormalizer.Normalize(selectedText);

        var beforeRuns = Core.Formatting.InlineSpanFormatApplier.SliceRuns(runs, 0, selStart);
        var afterRuns = Core.Formatting.InlineSpanFormatApplier.SliceRuns(runs, selEnd,
            Core.Formatting.InlineSpanFormatApplier.Flatten(runs).Length);

        BeginStructuralChange();

        UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
        Blocks.RemoveAt(index);

        int insertAt = index;

        if (beforeRuns.Count > 0 && !string.IsNullOrEmpty(Core.Formatting.InlineSpanFormatApplier.Flatten(beforeRuns)))
        {
            var beforeVm = BlockFactory.CreateBlock(block.Type, 0);
            beforeVm.CommitSpansFromEditor(beforeRuns);
            SubscribeToBlock(beforeVm);
            Blocks.Insert(insertAt++, beforeVm);
        }

        var eqVm = BlockFactory.CreateBlock(BlockType.Equation, 0);
        eqVm.EquationLatex = latex;
        SubscribeToBlock(eqVm);
        Blocks.Insert(insertAt++, eqVm);

        if (afterRuns.Count > 0 && !string.IsNullOrEmpty(Core.Formatting.InlineSpanFormatApplier.Flatten(afterRuns)))
        {
            var afterVm = BlockFactory.CreateBlock(block.Type, 0);
            afterVm.CommitSpansFromEditor(afterRuns);
            SubscribeToBlock(afterVm);
            Blocks.Insert(insertAt++, afterVm);
        }

        ReorderBlocks();
        CommitStructuralChange("Split to equation");
        NotifyBlocksChanged();

        Dispatcher.UIThread.Post(() => eqVm.IsFocused = true, DispatcherPriority.Render);
    }

    private void SubscribeToBlock(BlockViewModel block)
    {
        CancelPendingOrphanImageDeletesForBlock(block);
        block.ContentChanged += OnBlockContentChanged;
        block.PropertyChanged += OnBlockPropertyChanged;
        block.DeleteRequested += OnBlockDeleteRequested;
        block.DuplicateBlockRequested += OnDuplicateBlockRequested;
        block.NewBlockRequested += OnNewBlockRequested;
        block.NewBlockOfTypeRequested += OnNewBlockOfTypeRequested;
        block.NewBlockAboveRequested += OnNewBlockAboveRequested;
        block.DeleteAndFocusAboveRequested += OnDeleteAndFocusAboveRequested;
        block.FocusPreviousRequested += OnFocusPreviousRequested;
        block.FocusNextRequested += OnFocusNextRequested;
        block.MergeWithPreviousRequested += OnMergeWithPreviousRequested;
        block.ExitSplitBelowRequested += OnExitSplitBelowRequested;
        block.StructuralChangeStarting += OnStructuralChangeStarting;
        block.StructuralChangeCompleted += OnStructuralChangeCompleted;
        if (block is TwoColumnBlockViewModel tc)
        {
            tc.ColumnChildrenChanged += OnTwoColumnColumnChildrenChanged;
            foreach (var c in tc.LeftColumnBlocks)
            {
                BlockHierarchy.WireChildOwnership(tc, c, true);
                SubscribeToBlock(c);
            }
            foreach (var c in tc.RightColumnBlocks)
            {
                BlockHierarchy.WireChildOwnership(tc, c, false);
                SubscribeToBlock(c);
            }
        }
    }

    private void OnTwoColumnColumnChildrenChanged(TwoColumnBlockViewModel tc, bool leftColumn, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (BlockViewModel n in e.NewItems)
            {
                BlockHierarchy.WireChildOwnership(tc, n, leftColumn);
                SubscribeToBlock(n);
            }
        }
        if (e.OldItems != null)
        {
            foreach (BlockViewModel o in e.OldItems)
                UnsubscribeFromBlock(o, removeStoredImageIfOrphan: false);
        }
        UpdateListNumbers();
        NotifyBlocksChanged();
    }

    /// <param name="removeStoredImageIfOrphan">
    /// True when the block is permanently removed from the document (delete, merge, paste replace, etc.).
    /// False when detaching for reorder, column moves, undo/redo restore, or reparenting — the same block (or its image path) may still be in use.
    /// </param>
    private void UnsubscribeFromBlock(BlockViewModel block, bool removeStoredImageIfOrphan = false)
    {
        if (removeStoredImageIfOrphan)
            QueueDeleteOrphanImageForRemovedBlock(block);
        block.ContentChanged -= OnBlockContentChanged;
        block.PropertyChanged -= OnBlockPropertyChanged;
        block.DeleteRequested -= OnBlockDeleteRequested;
        block.DuplicateBlockRequested -= OnDuplicateBlockRequested;
        block.NewBlockRequested -= OnNewBlockRequested;
        block.NewBlockOfTypeRequested -= OnNewBlockOfTypeRequested;
        block.NewBlockAboveRequested -= OnNewBlockAboveRequested;
        block.DeleteAndFocusAboveRequested -= OnDeleteAndFocusAboveRequested;
        block.FocusPreviousRequested -= OnFocusPreviousRequested;
        block.FocusNextRequested -= OnFocusNextRequested;
        block.MergeWithPreviousRequested -= OnMergeWithPreviousRequested;
        block.ExitSplitBelowRequested -= OnExitSplitBelowRequested;
        block.StructuralChangeStarting -= OnStructuralChangeStarting;
        block.StructuralChangeCompleted -= OnStructuralChangeCompleted;
        if (block is TwoColumnBlockViewModel tc)
        {
            tc.ColumnChildrenChanged -= OnTwoColumnColumnChildrenChanged;
            foreach (var c in tc.LeftColumnBlocks.ToList())
                UnsubscribeFromBlock(c, removeStoredImageIfOrphan);
            foreach (var c in tc.RightColumnBlocks.ToList())
                UnsubscribeFromBlock(c, removeStoredImageIfOrphan);
        }
    }

    private void OnStructuralChangeStarting()
    {
        BeginStructuralChange();
    }

    private void OnStructuralChangeCompleted(string description)
    {
        CommitStructuralChange(description);
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlockViewModel.Type))
        {
            UpdateListNumbers();
            if (!_isRestoringFromHistory && _pendingSnapshot != null)
            {
                CommitStructuralChange("Change block type");
            }
            return;
        }

        if (e.PropertyName == nameof(BlockViewModel.IsChecked))
        {
            if (!_isRestoringFromHistory && _pendingSnapshot != null)
            {
                CommitStructuralChange("Toggle checklist");
            }
            return;
        }

        if (e.PropertyName != nameof(BlockViewModel.IsFocused)) return;
        if (sender is not BlockViewModel block) return;

        if (block.IsFocused)
        {
            var topIdx = BlockHierarchy.GetTopLevelIndex(Blocks, block);
            // Posted focus (e.g. new block after Enter) can run before the previous editor's LostFocus, so two
            // VMs may briefly have IsFocused. Clear only the last known owner — O(1); avoids N× EditableBlock
            // teardown (slash menu, toolbar posts) when the document has many blocks. RichTextEditor still
            // gates watermark paint on real keyboard/pointer focus.
            if (!string.IsNullOrEmpty(_focusedBlockId) && _focusedBlockId != block.Id)
            {
                var prev = BlockHierarchy.FindById(Blocks, _focusedBlockId);
                if (prev?.IsFocused == true)
                    prev.IsFocused = false;
            }

            _focusedBlockId = block.Id;
            _focusedBlockIndex = topIdx;

            // During cross-block text selection the editor controls which blocks have text
            // selection. Clearing it here (triggered by IsFocused changes on each block we
            // update) would fight with UpdateCrossBlockSelection and cause flicker.
            if (_isCrossBlockSelecting || _crossBlockArmed) return;
            // When applying format to cross-block selection we focus each block in turn; don't clear other blocks' selection.
            if (_isApplyingCrossBlockFormat) return;

            ClearBlockSelection();
            ClearTextSelectionInAllBlocksExcept(block);
        }
        else if (!string.IsNullOrEmpty(_focusedBlockId) && _focusedBlockId == block.Id)
        {
            _focusedBlockId = null;
            _focusedBlockIndex = -1;
        }
    }

    private void OnBlockContentChanged(BlockViewModel block)
    {
        ClearBlockSelection();
        var prev = block.PreviousContent;
        var prevRuns = block.PreviousSpans;
        block.PreviousContent = null;
        block.PreviousSpans = null;

        if (!_isRestoringFromHistory && _pendingSnapshot == null)
        {
            if (prev != null || prevRuns != null)
            {
                TrackTypingEdit(block, prev ?? block.Content, prevRuns);
            }
            else
            {
            }
        }
        else
        {
        }
        if (_findPanelVisible)
            RefreshFindMatchesAndHighlights();
        BlocksChanged?.Invoke();
    }

    private void OnBlockDeleteRequested(BlockViewModel block)
    {
        DeleteBlock(block);
    }

    private void OnDeleteAndFocusAboveRequested(BlockViewModel block)
    {
        DeleteBlock(block);
    }

    private void OnMergeWithPreviousRequested(BlockViewModel block)
    {
        var previousBlock = BlockHierarchy.FindPreviousInDocumentOrder(Blocks, block);
        if (previousBlock == null) return;

        if (_pendingSnapshot == null)
            BeginStructuralChange();
        var insertionPoint = previousBlock.Content?.Length ?? 0;
        var suffix = BlockEditorContentPolicy.MergeSuffixFromFollowingBlock(block.Content);
        previousBlock.Content = (previousBlock.Content ?? string.Empty) + suffix;

        if (block.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            col.Remove(block);
            BlockHierarchy.ClearChildOwnership(block);
            UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
            if (col.Count == 0)
            {
                var ph = BlockFactory.CreateBlock(BlockType.Text, 0);
                BlockHierarchy.WireChildOwnership(tc, ph, block.IsLeftColumn);
                col.Add(ph);
                SubscribeToBlock(ph);
            }
        }
        else
        {
            UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
            Blocks.Remove(block);
        }

        ReorderBlocks();
        CommitStructuralChange("Merge blocks");
        BlocksChanged?.Invoke();

        var caretTarget = insertionPoint;
        Dispatcher.UIThread.Post(
            () =>
            {
                previousBlock.PendingCaretIndex = caretTarget;
                previousBlock.IsFocused = true;
            },
            DispatcherPriority.Input);
    }

    private void OnExitSplitBelowRequested(BlockViewModel block, string? followingText)
    {
        if (block.OwnerTwoColumn is not TwoColumnBlockViewModel tc) return;
        var topIdx = Blocks.IndexOf(tc);
        if (topIdx < 0) return;

        if (_pendingSnapshot == null)
            BeginStructuralChange();

        var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
        var bodyEmpty = BlockEditorContentPolicy.IsVisuallyEmpty(block.Content ?? "");
        if (bodyEmpty && col.Contains(block))
        {
            col.Remove(block);
            BlockHierarchy.ClearChildOwnership(block);
            UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
            if (col.Count == 0)
            {
                var ph = BlockFactory.CreateBlock(BlockType.Text, 0);
                BlockHierarchy.WireChildOwnership(tc, ph, block.IsLeftColumn);
                col.Add(ph);
                SubscribeToBlock(ph);
            }
        }

        var nb = BlockFactory.CreateBlock(BlockType.Text, 0);
        if (!string.IsNullOrEmpty(followingText))
            nb.Content = followingText;
        SubscribeToBlock(nb);
        Blocks.Insert(topIdx + 1, nb);
        ReorderBlocks();
        CommitStructuralChange("Exit split");
        BlocksChanged?.Invoke();
        Dispatcher.UIThread.Post(() => nb.IsFocused = true, DispatcherPriority.Input);
    }

    private void DeleteBlock(BlockViewModel block)
    {
        if (_pendingSnapshot == null)
            BeginStructuralChange();

        if (block.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            var ci = col.IndexOf(block);
            if (ci < 0) return;

            if (ShouldUnwrapTwoColumnBecauseAllCellsEmpty(tc, block))
            {
                var topIdx = Blocks.IndexOf(tc);
                if (topIdx < 0) return;
                UnsubscribeFromBlock(tc, removeStoredImageIfOrphan: true);
                Blocks.RemoveAt(topIdx);
                var placeholder = BlockFactory.CreateBlock(BlockType.Text, 0);
                SubscribeToBlock(placeholder);
                Blocks.Insert(topIdx, placeholder);
                ReorderBlocks();
                CommitStructuralChange("Unwrap split");
                BlocksChanged?.Invoke();
                Dispatcher.UIThread.Post(() => placeholder.IsFocused = true, DispatcherPriority.Input);
                return;
            }

            if (ShouldUnwrapTwoColumnBecauseOneColumnEmptyOtherHasContent(tc, block))
            {
                var filledColumnIsLeft = !ColumnIsEntirelyVisuallyEmpty(tc, true) &&
                    ColumnIsEntirelyVisuallyEmpty(tc, false);
                UnwrapTwoColumnPromotingFilledColumn(tc, filledColumnIsLeft);
                CommitStructuralChange("Unwrap split");
                BlocksChanged?.Invoke();
                return;
            }

            if (col.Count == 1 && tc.LeftColumnBlocks.Count + tc.RightColumnBlocks.Count == 1)
            {
                var topIdx = Blocks.IndexOf(tc);
                if (topIdx < 0) return;
                UnsubscribeFromBlock(tc, removeStoredImageIfOrphan: true);
                Blocks.RemoveAt(topIdx);
                var placeholder = BlockFactory.CreateBlock(BlockType.Text, 0);
                SubscribeToBlock(placeholder);
                Blocks.Insert(topIdx, placeholder);
                ReorderBlocks();
                CommitStructuralChange("Delete block");
                BlocksChanged?.Invoke();
                Dispatcher.UIThread.Post(() => placeholder.IsFocused = true, DispatcherPriority.Input);
                return;
            }

            if (RemoveCellFromTwoColumnOrUnwrap(tc, block))
            {
                CommitStructuralChange("Unwrap split");
                BlocksChanged?.Invoke();
                return;
            }

            CommitStructuralChange("Delete block");
            BlocksChanged?.Invoke();
            var focusIdx = Math.Max(0, Math.Min(ci, col.Count - 1));
            Dispatcher.UIThread.Post(() => col[focusIdx].IsFocused = true, DispatcherPriority.Input);
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index == -1) return;

        if (Blocks.Count == 1)
        {
            if (block.Type == BlockType.Image)
                QueueDeleteOrphanImageForRemovedBlock(block);
            block.Content = string.Empty;
            block.Type = BlockType.Text;
            block.IsFocused = true;
            UpdateListNumbers();
            CommitStructuralChange("Clear block");
            return;
        }

        UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
        Blocks.Remove(block);

        var targetIndex = index > 0 ? index - 1 : 0;
        if (Blocks.Count > targetIndex)
        {
            Dispatcher.UIThread.Post(
                () => Blocks[targetIndex].IsFocused = true, 
                DispatcherPriority.Input);
        }

        ReorderBlocks();
        CommitStructuralChange("Delete block");
        BlocksChanged?.Invoke();
    }

    private static bool IsEffectivelyEmptyForSplitCollapse(BlockViewModel block)
    {
        if (block.Type == BlockType.Image)
        {
            if (!string.IsNullOrWhiteSpace(block.ImagePath)) return false;
            if (!string.IsNullOrWhiteSpace(block.Content)) return false;
            return true;
        }

        return BlockEditorContentPolicy.IsVisuallyEmpty(block.Content);
    }

    /// <summary>
    /// When every cell in the split is visually empty (including <paramref name="block"/>), backspace
    /// removes the whole TwoColumn and restores a single top-level paragraph.
    /// </summary>
    private static bool ShouldUnwrapTwoColumnBecauseAllCellsEmpty(TwoColumnBlockViewModel tc, BlockViewModel block)
    {
        if (!IsEffectivelyEmptyForSplitCollapse(block)) return false;
        foreach (var b in tc.LeftColumnBlocks)
        {
            if (ReferenceEquals(b, block)) continue;
            if (!IsEffectivelyEmptyForSplitCollapse(b)) return false;
        }
        foreach (var b in tc.RightColumnBlocks)
        {
            if (ReferenceEquals(b, block)) continue;
            if (!IsEffectivelyEmptyForSplitCollapse(b)) return false;
        }
        return true;
    }

    private static bool ColumnIsEntirelyVisuallyEmpty(TwoColumnBlockViewModel tc, bool leftColumn)
    {
        var col = leftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
        foreach (var b in col)
        {
            if (!IsEffectivelyEmptyForSplitCollapse(b))
                return false;
        }
        return true;
    }

    /// <summary>
    /// One column is only empty paragraphs; the other has real content — backspace in the empty column
    /// removes the split and promotes the filled column to top-level blocks.
    /// </summary>
    private static bool ShouldUnwrapTwoColumnBecauseOneColumnEmptyOtherHasContent(TwoColumnBlockViewModel tc,
        BlockViewModel block)
    {
        if (!IsEffectivelyEmptyForSplitCollapse(block)) return false;
        if (ColumnIsEntirelyVisuallyEmpty(tc, true) && !ColumnIsEntirelyVisuallyEmpty(tc, false) && block.IsLeftColumn)
            return true;
        if (ColumnIsEntirelyVisuallyEmpty(tc, false) && !ColumnIsEntirelyVisuallyEmpty(tc, true) && !block.IsLeftColumn)
            return true;
        return false;
    }

    private void UnwrapTwoColumnPromotingFilledColumn(TwoColumnBlockViewModel tc, bool filledColumnIsLeft)
    {
        var topIdx = Blocks.IndexOf(tc);
        if (topIdx < 0) return;

        var filledCol = filledColumnIsLeft ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
        var emptyCol = filledColumnIsLeft ? tc.RightColumnBlocks : tc.LeftColumnBlocks;

        var promoted = filledCol.ToList();
        foreach (var b in emptyCol.ToList())
        {
            UnsubscribeFromBlock(b, removeStoredImageIfOrphan: true);
            emptyCol.Remove(b);
        }
        foreach (var b in promoted)
        {
            UnsubscribeFromBlock(b, removeStoredImageIfOrphan: false);
            filledCol.Remove(b);
            BlockHierarchy.ClearChildOwnership(b);
        }

        UnsubscribeFromBlock(tc, removeStoredImageIfOrphan: false);
        Blocks.RemoveAt(topIdx);

        int insertAt = topIdx;
        foreach (var b in promoted)
        {
            SubscribeToBlock(b);
            Blocks.Insert(insertAt++, b);
        }

        ReorderBlocks();
        var focus = promoted.Count > 0 ? promoted[0] : null;
        if (focus != null)
            Dispatcher.UIThread.Post(() => focus.IsFocused = true, DispatcherPriority.Input);
    }

    /// <summary>Removes a cell from a split. If that column becomes empty and the other column has blocks, unwraps to top-level.
    /// Returns true when the split was removed via unwrap.</summary>
    private bool RemoveCellFromTwoColumnOrUnwrap(TwoColumnBlockViewModel tc, BlockViewModel block)
    {
        var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
        var ci = col.IndexOf(block);
        if (ci < 0) return false;

        UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
        col.RemoveAt(ci);
        BlockHierarchy.ClearChildOwnership(block);

        if (col.Count == 0)
        {
            var other = block.IsLeftColumn ? tc.RightColumnBlocks : tc.LeftColumnBlocks;
            if (other.Count > 0)
            {
                UnwrapTwoColumnPromotingFilledColumn(tc, !block.IsLeftColumn);
                return true;
            }

            var ph = BlockFactory.CreateBlock(BlockType.Text, 0);
            BlockHierarchy.WireChildOwnership(tc, ph, block.IsLeftColumn);
            col.Add(ph);
            SubscribeToBlock(ph);
        }

        ReorderBlocks();
        return false;
    }

    /// <summary>
    /// After the last non-empty block is dragged out, only empty placeholders remain — replace the split with one empty paragraph.
    /// </summary>
    private void TryCollapseSplitToSingleEmptyIfAllLeavesEmpty(TwoColumnBlockViewModel tc)
    {
        if (Blocks.IndexOf(tc) < 0) return;
        foreach (var b in tc.LeftColumnBlocks)
        {
            if (!IsEffectivelyEmptyForSplitCollapse(b))
                return;
        }
        foreach (var b in tc.RightColumnBlocks)
        {
            if (!IsEffectivelyEmptyForSplitCollapse(b))
                return;
        }
        var topIdx = Blocks.IndexOf(tc);
        if (topIdx < 0) return;
        UnsubscribeFromBlock(tc, removeStoredImageIfOrphan: true);
        Blocks.RemoveAt(topIdx);
        var placeholder = BlockFactory.CreateBlock(BlockType.Text, 0);
        SubscribeToBlock(placeholder);
        Blocks.Insert(topIdx, placeholder);
        ReorderBlocks();
    }

    /// <summary>
    /// Run after a block was detached to top-level so the split row can collapse (only empty cells left,
    /// or one column entirely empty and the other still has content — unwrap to a single column stack).
    /// Must run after <see cref="Blocks.Insert"/> for the dragged block so indices and subscriptions are consistent.
    /// </summary>
    private void TryCollapseSplitAfterDragOut(TwoColumnBlockViewModel? tc)
    {
        if (tc == null || Blocks.IndexOf(tc) < 0) return;
        TryCollapseSplitToSingleEmptyIfAllLeavesEmpty(tc);
        if (Blocks.IndexOf(tc) < 0) return;
        if (IsNativeTwoColumn(tc)) return;
        if (ColumnIsEntirelyVisuallyEmpty(tc, true) && !ColumnIsEntirelyVisuallyEmpty(tc, false))
            UnwrapTwoColumnPromotingFilledColumn(tc, false);
        else if (ColumnIsEntirelyVisuallyEmpty(tc, false) && !ColumnIsEntirelyVisuallyEmpty(tc, true))
            UnwrapTwoColumnPromotingFilledColumn(tc, true);
    }

    private static bool IsNativeTwoColumn(TwoColumnBlockViewModel tc)
    {
        if (!tc.Meta.TryGetValue(NativeTwoColumnMetaKey, out var raw) || raw == null)
            return false;
        if (raw is bool b)
            return b;
        if (raw is string s && bool.TryParse(s, out var parsed))
            return parsed;
        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var jsonParsed))
                return jsonParsed;
        }
        return false;
    }

    private void OnNewBlockRequested(BlockViewModel block, string? initialContent)
    {
        if (_pendingSnapshot == null)
            BeginStructuralChange();

        if (block.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            var i = col.IndexOf(block);
            if (i < 0) return;
            var newBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
            if (initialContent != null)
                newBlock.Content = initialContent;
            BlockHierarchy.WireChildOwnership(tc, newBlock, block.IsLeftColumn);
            col.Insert(i + 1, newBlock);
            SubscribeToBlock(newBlock);
            ReorderBlocks();
            CommitStructuralChange("Split block");
            BlocksChanged?.Invoke();
            newBlock.PendingCaretIndex = 0;
            Dispatcher.UIThread.Post(() => newBlock.IsFocused = true, DispatcherPriority.Render);
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index < 0) return;
        AddBlock(BlockType.Text, index + 1, initialContent);

        var newBlockIndex = index + 1;
        if (newBlockIndex < Blocks.Count)
        {
            var newBlock = Blocks[newBlockIndex];
            newBlock.PendingCaretIndex = 0;
            Dispatcher.UIThread.Post(
                () => newBlock.IsFocused = true, 
                DispatcherPriority.Render);
        }

        CommitStructuralChange("Split block");
        BlocksChanged?.Invoke();
    }

    private void OnNewBlockAboveRequested(BlockViewModel block, string? initialContent)
    {
        if (_pendingSnapshot == null)
            BeginStructuralChange();

        if (block.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            var i = col.IndexOf(block);
            if (i < 0) return;
            var newBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
            if (initialContent != null)
                newBlock.Content = initialContent;
            BlockHierarchy.WireChildOwnership(tc, newBlock, block.IsLeftColumn);
            col.Insert(i, newBlock);
            SubscribeToBlock(newBlock);
            ReorderBlocks();
            CommitStructuralChange("Insert block above");
            BlocksChanged?.Invoke();
            newBlock.PendingCaretIndex = 0;
            Dispatcher.UIThread.Post(() =>
            {
                newBlock.IsFocused = false;
                newBlock.IsFocused = true;
            }, DispatcherPriority.Loaded);
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index < 0) return;
        AddBlock(BlockType.Text, index, initialContent);

        // `block` is still the same instance, now at index+1. If the guard failed before, we never focused.
        if (Blocks.IndexOf(block) < 0) return;

        if (!block.PendingCaretIndex.HasValue)
            block.PendingCaretIndex = 0;

        CommitStructuralChange("Insert block above");
        BlocksChanged?.Invoke();

        // After BlockRows rebuild, focus the shifted block. VM only notifies IsFocused when value *changes*;
        // if LostFocus hasn't cleared yet, IsFocused=true is a no-op — toggle false→true so FocusTextBox runs.
        Dispatcher.UIThread.Post(() =>
        {
            if (!block.PendingCaretIndex.HasValue)
                block.PendingCaretIndex = 0;
            block.IsFocused = false;
            block.IsFocused = true;
        }, DispatcherPriority.Loaded);
    }

    private void OnNewBlockOfTypeRequested(BlockViewModel block, BlockType type, string? initialContent)
    {
        BeginStructuralChange();

        if (block.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            var i = col.IndexOf(block);
            if (i < 0) return;
            var newBlock = BlockFactory.CreateBlock(type, 0);
            if (initialContent != null)
                newBlock.Content = initialContent;
            BlockHierarchy.WireChildOwnership(tc, newBlock, block.IsLeftColumn);
            col.Insert(i + 1, newBlock);
            SubscribeToBlock(newBlock);
            ReorderBlocks();
            CommitStructuralChange("New block");
            BlocksChanged?.Invoke();
            newBlock.PendingCaretIndex = 0;
            Dispatcher.UIThread.Post(() => newBlock.IsFocused = true, DispatcherPriority.Render);
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index < 0) return;
        AddBlock(type, index + 1, initialContent);

        var newBlockIndex = index + 1;
        if (newBlockIndex < Blocks.Count)
        {
            var newBlock = Blocks[newBlockIndex];
            newBlock.PendingCaretIndex = 0;
            Dispatcher.UIThread.Post(
                () => newBlock.IsFocused = true, 
                DispatcherPriority.Render);
        }

        CommitStructuralChange("New block");
        BlocksChanged?.Invoke();
    }

    private void OnDuplicateBlockRequested(BlockViewModel block)
    {
        _ = DuplicateImageBlockAsync(block);
    }

    private async Task DuplicateImageBlockAsync(BlockViewModel block)
    {
        if (block.Type != BlockType.Image) return;
        var svc = ResolveImageAssetService();
        if (svc == null) return;

        var srcPath = block.ImagePath;
        if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) return;

        var index = Blocks.IndexOf(block);
        if (index < 0) return;

        var newVm = BlockFactory.CreateBlock(BlockType.Image, 0);
        var result = await svc.ImportAndCopyAsync(srcPath, newVm.Id).ConfigureAwait(true);
        if (!result.IsSuccess || string.IsNullOrEmpty(result.Value)) return;

        BeginStructuralChange();

        newVm.ImagePath = result.Value;
        newVm.ImageWidth = block.ImageWidth;
        newVm.ImageAlign = string.IsNullOrEmpty(block.ImageAlign) ? "left" : block.ImageAlign;
        newVm.SetSpans(new List<InlineSpan>(block.Spans));

        SubscribeToBlock(newVm);
        Blocks.Insert(index + 1, newVm);
        ReorderBlocks();
        ClearBlockSelection();
        CommitStructuralChange("Duplicate image block");
        BlocksChanged?.Invoke();

        Dispatcher.UIThread.Post(() => newVm.IsFocused = true, DispatcherPriority.Input);
    }

    private void OnFocusPreviousRequested(BlockViewModel block, double? caretPixelX)
    {
        var prev = BlockHierarchy.FindPreviousInDocumentOrder(Blocks, block);
        if (prev == null) return;
        block.IsFocused = false;
        if (caretPixelX.HasValue)
        {
            prev.PendingCaretPixelX = caretPixelX.Value;
            prev.PendingCaretPlaceOnLastLine = true;
        }
        Dispatcher.UIThread.Post(() => prev.IsFocused = true, DispatcherPriority.Input);
    }

    private void OnFocusNextRequested(BlockViewModel block, double? caretPixelX)
    {
        var next = BlockHierarchy.FindNextInDocumentOrder(Blocks, block);
        if (next == null) return;
        block.IsFocused = false;
        if (caretPixelX.HasValue)
        {
            next.PendingCaretPixelX = caretPixelX.Value;
            next.PendingCaretPlaceOnLastLine = false;
        }
        Dispatcher.UIThread.Post(() => next.IsFocused = true, DispatcherPriority.Input);
    }

    private void ReorderBlocks()
    {
        for (int i = 0; i < Blocks.Count; i++)
        {
            Blocks[i].Order = i;
            if (Blocks[i] is TwoColumnBlockViewModel tc)
            {
                for (int j = 0; j < tc.LeftColumnBlocks.Count; j++)
                    tc.LeftColumnBlocks[j].Order = j;
                for (int j = 0; j < tc.RightColumnBlocks.Count; j++)
                    tc.RightColumnBlocks[j].Order = j;
            }
        }

        if (BlockHierarchy.EnumerateInDocumentOrder(Blocks).Any(b => b.Type == BlockType.NumberedList))
            UpdateListNumbers();
    }

    private void UpdateListNumbers()
    {
        int listNumber = 1;
        var ordered = BlockHierarchy.EnumerateInDocumentOrder(Blocks).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Type == BlockType.NumberedList)
            {
                if (i == 0 || ordered[i - 1].Type != BlockType.NumberedList)
                    listNumber = 1;
                ordered[i].ListNumberIndex = listNumber++;
            }
            else
                listNumber = 1;
        }
    }

    public void NotifyBlocksChanged()
    {
        if (_findPanelVisible)
            RefreshFindMatchesAndHighlights();
        BlocksChanged?.Invoke();
    }

    public event System.Action? BlocksChanged;
    public new event PropertyChangedEventHandler? PropertyChanged;

    #region Drag-box block selection (Mode 2)

    /// <summary>
    /// Clears block selection (IsSelected) on all blocks. Call when the user performs
    /// another action (focus a block, edit text, drag to reorder, etc.).
    /// </summary>
    public void ClearBlockSelection()
    {
        foreach (var block in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
            block.IsSelected = false;
        _blockDragHandleSelectionAnchorIndex = -1;
    }

    /// <summary>Selects exactly one block (e.g. click on drag handle). Clears all text selection.</summary>
    public void SelectSingleBlock(BlockViewModel vm)
    {
        if (BlockHierarchy.FindById(Blocks, vm.Id) == null) return;
        ClearTextSelectionInAllBlocksExcept(null);
        foreach (var block in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
            block.IsSelected = ReferenceEquals(block, vm);
        _blockDragHandleSelectionAnchorIndex = GetDocumentOrderBlocks().IndexOf(vm);
    }

    /// <summary>
    /// Drag handle release (no drag): plain = single; Ctrl/Meta = toggle; Shift = range from anchor;
    /// Ctrl+Shift+range = add that range to the current selection.
    /// </summary>
    public void SelectBlockFromDragHandle(BlockViewModel vm, KeyModifiers modifiers)
    {
        if (BlockHierarchy.FindById(Blocks, vm.Id) == null) return;
        ClearTextSelectionInAllBlocksExcept(null);
        var doc = GetDocumentOrderBlocks();
        int idx = doc.IndexOf(vm);
        if (idx < 0) return;

        bool toggle = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);

        if (toggle && !shift)
        {
            vm.IsSelected = !vm.IsSelected;
            _blockDragHandleSelectionAnchorIndex = idx;
            return;
        }

        if (shift)
        {
            int anchor = _blockDragHandleSelectionAnchorIndex >= 0 ? _blockDragHandleSelectionAnchorIndex : GetShiftRangeAnchorIndex(idx);
            int lo = Math.Min(anchor, idx);
            int hi = Math.Max(anchor, idx);
            if (toggle)
            {
                for (int i = lo; i <= hi; i++)
                    doc[i].IsSelected = true;
            }
            else
            {
                for (int i = 0; i < doc.Count; i++)
                    doc[i].IsSelected = i >= lo && i <= hi;
            }
            _blockDragHandleSelectionAnchorIndex = anchor;
            return;
        }

        SelectSingleBlock(vm);
    }

    private int GetShiftRangeAnchorIndex(int clickedIndex)
    {
        var doc = GetDocumentOrderBlocks();
        for (int i = 0; i < doc.Count; i++)
        {
            if (doc[i].IsFocused)
                return i;
        }
        return clickedIndex;
    }

    /// <summary>
    /// Shows a live clone of the block (same as notes sidebar ghost) at reduced opacity — avoids RenderTargetBitmap text rasterization artifacts.
    /// </summary>
    internal void BeginBlockDragGhost(EditableBlock source, PointerEventArgs e)
    {
        EndBlockDragGhost();

        var overlay = this.FindControl<LayoutOverlayPanel>("BlockDragGhostOverlay");
        if (overlay == null) return;

        double w = source.Bounds.Width;
        double h = source.Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            var snap = source.BlockDragSnapshotTarget;
            w = snap.Bounds.Width;
            h = snap.Bounds.Height;
        }

        if (w <= 0 || h <= 0) return;

        var ghostBlock = new EditableBlock
        {
            DataContext = source.DataContext,
            Width = w,
            Height = h,
            Focusable = false,
            IsHitTestVisible = false,
            IsTabStop = false
        };

        var ghost = new Border
        {
            Child = ghostBlock,
            Opacity = BlockDragGhostOpacity,
            IsHitTestVisible = false,
            BoxShadow = BoxShadows.Parse("0 4 16 0 #40000000"),
            CornerRadius = new CornerRadius(4),
            Width = w,
            Height = h
        };

        var pointerInOverlay = e.GetPosition(overlay);
        var originPoint = source.TranslatePoint(new Point(0, 0), overlay) ?? new Point(0, 0);
        _blockDragGhostPointerOffset = pointerInOverlay - originPoint;

        Canvas.SetLeft(ghost, pointerInOverlay.X - _blockDragGhostPointerOffset.X);
        Canvas.SetTop(ghost, pointerInOverlay.Y - _blockDragGhostPointerOffset.Y);

        overlay.Children.Add(ghost);
        _blockDragGhostBorder = ghost;
        _blockDragGhostOverlay = overlay;
        overlay.InvalidateArrange();
    }

    /// <summary>
    /// Positions the reorder ghost using editor-space coordinates (same as <see cref="EditableBlock"/> DragOver).
    /// </summary>
    internal void UpdateBlockDragGhostFromEditorPoint(Point cursorPosInEditor)
    {
        if (_blockDragGhostBorder == null || _blockDragGhostOverlay == null) return;
        var pt = this.TranslatePoint(cursorPosInEditor, _blockDragGhostOverlay);
        if (!pt.HasValue) return;
        Canvas.SetLeft(_blockDragGhostBorder, pt.Value.X - _blockDragGhostPointerOffset.X);
        Canvas.SetTop(_blockDragGhostBorder, pt.Value.Y - _blockDragGhostPointerOffset.Y);
        _blockDragGhostOverlay.InvalidateArrange();

        MaybeAutoScrollViewportDuringBlockDrag(cursorPosInEditor);
    }

    private void MaybeAutoScrollViewportDuringBlockDrag(Point cursorPosInEditor)
    {
        var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer == null) return;

        var ptInScroll = this.TranslatePoint(cursorPosInEditor, scrollViewer);
        if (!ptInScroll.HasValue) return;

        double scrollHeight = scrollViewer.Bounds.Height;
        if (scrollHeight <= 0) return;

        double y = ptInScroll.Value.Y;

        if (y < BlockDragAutoScrollZone)
        {
            double intensity = 1.0 - (y / BlockDragAutoScrollZone);
            _blockDragAutoScrollDirection = -BlockDragAutoScrollStep * (1 + intensity);
            EnsureBlockDragAutoScrollTimer();
        }
        else if (y > scrollHeight - BlockDragAutoScrollZone)
        {
            double intensity = 1.0 - ((scrollHeight - y) / BlockDragAutoScrollZone);
            _blockDragAutoScrollDirection = BlockDragAutoScrollStep * (1 + intensity);
            EnsureBlockDragAutoScrollTimer();
        }
        else
        {
            StopBlockDragAutoScroll();
        }
    }

    private void EnsureBlockDragAutoScrollTimer()
    {
        if (_blockDragAutoScrollTimer != null) return;
        _blockDragAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BlockDragAutoScrollIntervalMs) };
        _blockDragAutoScrollTimer.Tick += OnBlockDragAutoScrollTick;
        _blockDragAutoScrollTimer.Start();
    }

    private void OnBlockDragAutoScrollTick(object? sender, EventArgs e)
    {
        if (_blockDragGhostBorder == null)
        {
            StopBlockDragAutoScroll();
            return;
        }

        var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer == null)
        {
            StopBlockDragAutoScroll();
            return;
        }

        var current = scrollViewer.Offset;
        double maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        double newY = Math.Clamp(current.Y + _blockDragAutoScrollDirection, 0, maxY);
        scrollViewer.Offset = new Vector(current.X, newY);
    }

    private void StopBlockDragAutoScroll()
    {
        if (_blockDragAutoScrollTimer != null)
        {
            _blockDragAutoScrollTimer.Tick -= OnBlockDragAutoScrollTick;
            _blockDragAutoScrollTimer.Stop();
            _blockDragAutoScrollTimer = null;
        }
        _blockDragAutoScrollDirection = 0;
    }

    internal void EndBlockDragGhost()
    {
        StopBlockDragAutoScroll();

        if (_blockDragGhostBorder != null && _blockDragGhostOverlay != null)
        {
            _blockDragGhostOverlay.Children.Remove(_blockDragGhostBorder);
            _blockDragGhostOverlay.InvalidateArrange();
        }

        _blockDragGhostBorder = null;
        _blockDragGhostOverlay = null;
    }

    /// <summary>
    /// Arms box-select from an external control (e.g. the ScrollViewer gutter outside the editor column).
    /// <paramref name="pressPointInEditor"/> must already be in editor coordinates.
    /// Returns true if armed (caller should capture the pointer on its own control and forward
    /// subsequent PointerMoved/Released via <see cref="HandleExternalPointerMoved"/> and
    /// <see cref="HandleExternalPointerReleased"/>).
    /// </summary>
    public bool ArmExternalBoxSelect(Point pressPointInEditor, IPointer pointer)
    {
        _boxSelectStart = pressPointInEditor;
        var overlay = _selectionBoxBorder?.GetVisualParent() as Visual;
        _boxSelectStartInOverlay = overlay != null && this.TranslatePoint(pressPointInEditor, overlay) is { } ov ? ov : pressPointInEditor;
        _boxSelectArmed = true;
        _isBoxSelecting = false;
        ClearBlockSelection();
        ClearTextSelectionInAllBlocksExcept(null);
        pointer.Capture(this);
        return true;
    }

    /// <summary>
    /// Tunnel: when press is inside a block, capture immediately for cross-block text selection so we receive
    /// PointerMoved (TextBox would otherwise capture and we would never see moves). When press is on empty
    /// space, arm box-select and capture in PointerMoved once threshold is exceeded.
    /// </summary>
    private void Editor_PointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (IsReadOnly)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Double/triple clicks: clear all blocks so only the block under the tap will get word/line selection from TextBox.
        if (e.ClickCount > 1)
        {
            var pt = e.GetPosition(this);
            if (IsPointInsideAnyBlock(pt))
            {
                ClearBlockSelection();
                ClearTextSelectionInAllBlocksExcept(null);
            }
            return;
        }

        // If the press originated on the drag handle, let it propagate so DoDragDrop can run.
        var source = e.Source as Visual;
        bool hitIsDragHandle = source != null &&
            (source is Border { Tag: "DragHandle" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is "DragHandle"));
        if (hitIsDragHandle) return;

        // Insert-below gutter: must not capture pointer or Tapped/click never reaches the border.
        bool hitIsAddBlockBelow = source != null &&
            (source is Border { Tag: "AddBlockBelow" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is "AddBlockBelow"));
        if (hitIsAddBlockBelow) return;

        // Image block width resize strip — must not capture here or the first drag is eaten by cross-block selection.
        bool hitIsImageResizeHandle = source != null &&
            (source is Border { Tag: "ImageResizeHandle" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is "ImageResizeHandle"));
        if (hitIsImageResizeHandle) return;

        // Column splitter sits in the gutter between cells; not inside EditableBlock hit — would otherwise arm box-select and steal capture.
        bool hitIsColumnSplitHandle = source != null &&
            (source is Border { Tag: "ColumnSplitHandle" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => Equals(b.Tag, "ColumnSplitHandle")));
        if (hitIsColumnSplitHandle) return;

        // Tap strip below column stacks (adds a block in that column); must not arm box-select.
        if (IsSplitColumnBottomTapBorder(source))
            return;

        // Let native CheckBox handling run (checklist toggles) instead of capturing in editor tunnel.
        bool hitIsCheckBox = source is CheckBox || (source != null && source.GetVisualAncestors().Any(a => a is CheckBox));
        if (hitIsCheckBox) return;

        var pos = e.GetPosition(this);

        if (IsPointInsideAnyBlock(pos))
        {
            // Press inside a block: arm cross-block selection and capture so we get PointerMoved
            var docList = GetDocumentOrderBlocks();
            var blockIndex = GetBlockIndexAtPoint(pos);
            if (blockIndex < 0 || blockIndex >= docList.Count) return;

            var vm = docList[blockIndex];
            var editableBlock = GetEditableBlockForViewModel(vm);
            if (editableBlock == null) return;

            // Image block: avoid capturing on first press so toolbar/flyouts and caption clicks work;
            // programmatic focus uses HoverHost unless PendingCaretIndex targets the caption.
            if (vm.Type == BlockType.Image && !vm.IsFocused)
            {
                ClearBlockSelection();
                ClearTextSelectionInAllBlocksExcept(null);
                if (TryFindImageCaptionRichEditor(source, out var captionRte))
                {
                    var idx = captionRte.HitTestPoint(e.GetPosition(captionRte));
                    vm.PendingCaretIndex = Math.Clamp(idx, 0, captionRte.TextLength);
                }
                vm.IsFocused = true;
                return;
            }

            // If this block already has focus, let the event reach RichTextEditor so it can set
            // the caret from the click (HitTestPoint). Otherwise we'd set Handled and the caret would never move.
            if (vm.IsFocused)
            {
                ClearTextSelectionInAllBlocksExcept(null);
                ClearBlockSelection();

                // If the press landed outside the RichTextEditor itself (e.g. in block padding),
                // the editor won't receive OnPointerPressed. Initiate drag-select manually so the
                // full block width acts as a hit target for starting text selection.
                // Image blocks: caption is the only RTE — clicks on the bitmap/toolbar/resize are not
                // "padding"; mapping them into StartDragSelect marks Handled and breaks align/menu/reorder.
                if (vm.Type != BlockType.Image)
                {
                    var richEditor = editableBlock.TryGetRichTextEditor();
                    if (richEditor != null)
                    {
                        // Bounds-based checks are not enough: in right-side whitespace the pointer can be inside
                        // the editor rectangle but hit-test a parent (Border/Grid), so RichTextEditor never gets
                        // OnPointerPressed. Use the actual event source ancestry instead.
                        bool sourceInsideEditor = source != null
                            && (ReferenceEquals(source, richEditor)
                                || source.GetVisualAncestors().Any(a => ReferenceEquals(a, richEditor)));
                        if (!sourceInsideEditor)
                        {
                            var pointInFocusedBlock = this.TranslatePoint(pos, editableBlock);
                            if (pointInFocusedBlock.HasValue)
                            {
                                var paddingCharIndex = editableBlock.GetCharacterIndexFromPoint(pointInFocusedBlock.Value);
                                paddingCharIndex = Math.Clamp(paddingCharIndex, 0, editableBlock.GetAnchorCharClampMax());
                                richEditor.StartDragSelect(paddingCharIndex, e.Pointer);
                                _crossBlockAnchorBlock = vm;
                                _crossBlockAnchorBlockIndex = blockIndex;
                                _crossBlockAnchorCharIndex = paddingCharIndex;
                                _crossBlockStartPoint = pos;
                                _crossBlockArmed = true;
                                _isCrossBlockSelecting = false;
                                e.Handled = true;
                            }
                        }
                    }
                }
                return;
            }

            var pointInBlock = this.TranslatePoint(pos, editableBlock);
            if (!pointInBlock.HasValue) return;

            var charIndex = editableBlock.GetCharacterIndexFromPoint(pointInBlock.Value);

            ClearBlockSelection();
            _crossBlockAnchorBlock = vm;
            _crossBlockAnchorBlockIndex = blockIndex;
            _crossBlockAnchorCharIndex = Math.Clamp(charIndex, 0, editableBlock.GetAnchorCharClampMax());
            _crossBlockArmed = true;
            _isCrossBlockSelecting = false;

            // Clear all blocks first so the other block's selection always breaks; then set this block's caret.
            ClearTextSelectionInAllBlocksExcept(null);
            editableBlock.ApplyTextSelection(_crossBlockAnchorCharIndex, _crossBlockAnchorCharIndex);
            // Set PendingCaretIndex before IsFocused so FocusTextBox lands at the click position
            // directly — without this it would snap to the end first, causing a visible flicker.
            vm.PendingCaretIndex = _crossBlockAnchorCharIndex;
            vm.IsFocused = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Arm box-select — capture immediately to prevent ScrollViewer from stealing the gesture
        _boxSelectStart = pos;
        var overlay = _selectionBoxBorder?.GetVisualParent() as Visual;
        _boxSelectStartInOverlay = overlay != null ? e.GetPosition(overlay) : pos;
        _boxSelectArmed = true;
        _isBoxSelecting = false;

        ClearBlockSelection();
        ClearTextSelectionInAllBlocksExcept(null);
        e.Pointer.Capture(this);
    }

    private static bool TryFindImageCaptionRichEditor(Visual? source, [NotNullWhen(true)] out RichTextEditor? captionRte)
    {
        captionRte = null;
        if (source == null) return false;
        if (source is RichTextEditor r0 && r0.Tag is string t0 && t0 == "BlockEditorImageCaption")
        {
            captionRte = r0;
            return true;
        }

        var r1 = source.GetVisualAncestors().OfType<RichTextEditor>()
            .FirstOrDefault(x => x.Tag is string tag && tag == "BlockEditorImageCaption");
        if (r1 == null) return false;
        captionRte = r1;
        return true;
    }

    /// <summary>
    /// Returns true if the point (in editor coordinates) falls within a block's interactive hit surface
    /// (for image blocks: handle + content width, not full-row gutters beside a narrow image).
    /// </summary>
    private bool IsPointInsideAnyBlock(Point point)
    {
        foreach (var vm in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            var editable = GetEditableBlockForViewModel(vm);
            if (editable == null) continue;
            var local = this.TranslatePoint(point, editable);
            if (!local.HasValue) continue;
            if (editable.IsPointerHitInsideBlock(local.Value))
                return true;
        }
        return false;
    }

    private bool IsPointInsideBlockSurface(BlockViewModel block, Point pointInEditor)
    {
        var editable = GetEditableBlockForViewModel(block);
        if (editable == null)
            return false;
        var local = this.TranslatePoint(pointInEditor, editable);
        return local.HasValue && editable.IsPointerHitInsideBlock(local.Value);
    }

    private static bool IsSplitColumnBottomTapBorder(Visual? source)
    {
        if (source == null) return false;
        if (source is Border b0 && b0.Tag is string t0 && t0.StartsWith("SplitColumnBottom", StringComparison.Ordinal))
            return true;
        return source.GetVisualAncestors().OfType<Border>()
            .Any(b => b.Tag is string t && t.StartsWith("SplitColumnBottom", StringComparison.Ordinal));
    }

    private List<BlockViewModel> GetDocumentOrderBlocks() =>
        BlockHierarchy.EnumerateInDocumentOrder(Blocks).ToList();

    /// <summary>
    /// Bubble: clear selection; arm cross-block text selection when press is inside a TextBox.
    /// Skipped when we already armed and captured in Tunnel (cross-block).
    /// </summary>
    private void Editor_PointerPressedBubble(object? sender, PointerPressedEventArgs e)
    {
        if (IsReadOnly)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (_boxSelectArmed) return;
        // Already armed and captured in Tunnel for cross-block selection
        if (_crossBlockArmed) return;

        var source = e.Source as Visual;
        // Drag handle uses press+move threshold; selection is applied on release — do not clear here.
        bool hitIsDragHandle = source != null &&
            (source is Border { Tag: "DragHandle" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is "DragHandle"));
        if (hitIsDragHandle) return;

        ClearBlockSelection();

        bool hitIsInTextBox = source is TextBox || source is RichTextEditor || (source != null && source.GetVisualAncestors().Any(a => a is TextBox || a is RichTextEditor));
        if (!hitIsInTextBox || source == null) return;

        var textBox = source as TextBox ?? source.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        var richTextEditor = source as RichTextEditor ?? source.GetVisualAncestors().OfType<RichTextEditor>().FirstOrDefault();
        var editableBlock = source as EditableBlock ?? source.GetVisualAncestors().OfType<EditableBlock>().FirstOrDefault();
        if ((textBox == null && richTextEditor == null) || editableBlock == null || editableBlock.DataContext is not BlockViewModel vm || BlockHierarchy.FindById(Blocks, vm.Id) == null)
            return;

        // Image caption editor; arming cross-block breaks drag-select and steals PointerMoved.
        if (vm.Type == BlockType.Image
            && ((textBox?.Tag is string t1 && t1 == "BlockEditorImageCaption")
                || (richTextEditor?.Tag is string t2 && t2 == "BlockEditorImageCaption")))
            return;

        int caretIndex = textBox?.CaretIndex ?? richTextEditor?.CaretIndex ?? 0;
        int textLength = textBox?.Text?.Length ?? richTextEditor?.TextLength ?? 0;

        // Arm cross-block select — capture happens in PointerMoved once drag leaves the source block
        _crossBlockAnchorBlock = vm;
        _crossBlockAnchorBlockIndex = GetDocumentOrderBlocks().IndexOf(vm);
        _crossBlockAnchorCharIndex = Math.Clamp(caretIndex, 0, textLength);
        _crossBlockStartPoint = e.GetPosition(this);
        _crossBlockArmed = true;
        _isCrossBlockSelecting = false;
        // Do NOT capture or mark handled here — let the TextBox handle its own click normally
    }

    private void Editor_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsReadOnly)
            return;
        if (_isColumnSplitResizing)
            return;

        if (_blockDragGhostBorder != null && _blockDragGhostOverlay != null)
        {
            var pos = e.GetPosition(_blockDragGhostOverlay);
            Canvas.SetLeft(_blockDragGhostBorder, pos.X - _blockDragGhostPointerOffset.X);
            Canvas.SetTop(_blockDragGhostBorder, pos.Y - _blockDragGhostPointerOffset.Y);
            _blockDragGhostOverlay.InvalidateArrange();
        }

        // Only process if at least one selection mode is armed or active
        if (!_boxSelectArmed && !_isBoxSelecting && !_crossBlockArmed && !_isCrossBlockSelecting)
            return;

        // Convert position from the event source to editor coordinates
        var current = e.GetPosition(this);

        // TopLevel tunnel runs for every move app-wide. When a child (RichTextEditor) owns capture we usually
        // skip editor-level selection; however, if cross-block is armed, hand off once pointer leaves the
        // anchor block so selection can continue across blocks.
        if (e.Pointer.Captured != null && !ReferenceEquals(e.Pointer.Captured, this))
        {
            if (_crossBlockArmed && _crossBlockAnchorBlock != null && !IsPointInsideBlockSurface(_crossBlockAnchorBlock, current))
                e.Pointer.Capture(this);
            else
                return;
        }

        // Box selection: activate once movement exceeds threshold
        if (_boxSelectArmed)
        {
            var dx = current.X - _boxSelectStart.X;
            var dy = current.Y - _boxSelectStart.Y;
            if (Math.Sqrt(dx * dx + dy * dy) >= BoxSelectThreshold)
            {
                _boxSelectArmed = false;
                _isBoxSelecting = true;
                e.Pointer.Capture(this);
                if (_selectionBoxBorder != null)
                {
                    _selectionBoxBorder.IsVisible = true;
                    Canvas.SetLeft(_selectionBoxBorder, _boxSelectStartInOverlay.X);
                    Canvas.SetTop(_selectionBoxBorder, _boxSelectStartInOverlay.Y);
                    _selectionBoxBorder.Width = 0;
                    _selectionBoxBorder.Height = 0;
                    (_selectionBoxBorder.GetVisualParent() as LayoutOverlayPanel)?.InvalidateArrange();
                }
            }
        }

        if (_isBoxSelecting)
        {
            var overlay = _selectionBoxBorder?.GetVisualParent() as Visual;
            Point endInOverlay = overlay != null ? e.GetPosition(overlay) : current;
            UpdateSelectionBoxVisual(_boxSelectStartInOverlay, endInOverlay);
            UpdateBoxSelection(_boxSelectStart, current);
            e.Handled = true;
            return;
        }

        // Cross-block text selection: on first move we enter selecting mode (we already have capture from Tunnel).
        if (_crossBlockArmed && _crossBlockAnchorBlock != null)
        {
            if (_crossBlockAnchorBlock.Type != BlockType.Image)
                _isCrossBlockSelecting = true;
            _crossBlockArmed = false;
        }

        if (_isCrossBlockSelecting && _crossBlockAnchorBlock != null)
        {
            UpdateCrossBlockSelection(current);
            e.Handled = true;
        }
    }

    private void Editor_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (IsReadOnly)
            return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;

        var wasBoxSelecting = _isBoxSelecting;
        var wasBoxArmed = _boxSelectArmed;
        var wasCrossBlockSelecting = _isCrossBlockSelecting;
        var wasArmedButNotDragged = _crossBlockArmed && !_isCrossBlockSelecting && _crossBlockAnchorBlock != null;
        var clickAnchorBlock = _crossBlockAnchorBlock;
        var clickAnchorChar = _crossBlockAnchorCharIndex;

        _boxSelectArmed = false;
        _crossBlockArmed = false;
        _isBoxSelecting = false;
        _isCrossBlockSelecting = false;
        _crossBlockAnchorBlock = null;
        _crossBlockAnchorBlockIndex = -1;
        _lastCrossBlockCurrentIndex = -1;

        if (wasBoxSelecting)
        {
            e.Pointer.Capture(null);
            if (_selectionBoxBorder != null)
                _selectionBoxBorder.IsVisible = false;
        }
        else if (wasBoxArmed)
        {
            // Plain click on empty space: if below blocks, add new block and focus it
            var belowArea = this.FindControl<Border>("BelowBlocksArea");
            if (belowArea != null)
            {
                var topLeft = belowArea.TranslatePoint(new Point(0, 0), this);
                if (topLeft.HasValue)
                {
                    var rect = new Rect(topLeft.Value.X, topLeft.Value.Y, belowArea.Bounds.Width, belowArea.Bounds.Height);
                    if (rect.Contains(_boxSelectStart))
                    {
                        // Don't add a new block if the block above (last block) is empty
                        var lastIsEmpty = IsLastBlockEmptyForBelowBlocksAreaClick(Blocks);
                        if (!lastIsEmpty)
                        {
                            AddBlock(BlockType.Text, Blocks.Count);
                            if (Blocks.Count > 0)
                            {
                                var newBlock = Blocks[Blocks.Count - 1];
                                Avalonia.Threading.Dispatcher.UIThread.Post(
                                    () => newBlock.IsFocused = true,
                                    Avalonia.Threading.DispatcherPriority.Render);
                            }
                        }
                    }
                }
            }
            e.Pointer.Capture(null);
        }
        else if (wasCrossBlockSelecting)
        {
            e.Pointer.Capture(null);
            if (clickAnchorBlock != null)
            {
                var anchorIndex = Blocks.IndexOf(clickAnchorBlock);
                if (anchorIndex >= 0)
                {
                    var anchorBlock = GetEditableBlockForViewModel(Blocks[anchorIndex]);
                    anchorBlock?.NotifySelectionChangedByEditor();
                }
            }
        }
        else if (wasArmedButNotDragged && clickAnchorBlock != null)
        {
            // Plain click: focus+caret were already set at press time via PendingCaretIndex.
            // Just release the pointer capture that the tunnel handler acquired.
            e.Pointer.Capture(null);
        }
    }

    /// <param name="start">Start point in selection overlay coordinate space.</param>
    /// <param name="end">End point in selection overlay coordinate space.</param>
    private void UpdateSelectionBoxVisual(Point start, Point end)
    {
        if (_selectionBoxBorder == null) return;
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        Canvas.SetLeft(_selectionBoxBorder, x);
        Canvas.SetTop(_selectionBoxBorder, y);
        _selectionBoxBorder.Width = width;
        _selectionBoxBorder.Height = height;
        (_selectionBoxBorder.GetVisualParent() as LayoutOverlayPanel)?.InvalidateArrange();
    }

    private void UpdateBoxSelection(Point start, Point end)
    {
        double minX = Math.Min(start.X, end.X);
        double maxX = Math.Max(start.X, end.X);
        double minY = Math.Min(start.Y, end.Y);
        double maxY = Math.Max(start.Y, end.Y);
        var selectionRect = new Rect(minX, minY, maxX - minX, maxY - minY);

        var doc = GetDocumentOrderBlocks();
        for (int i = 0; i < doc.Count; i++)
        {
            var editable = GetEditableBlockForViewModel(doc[i]);
            Rect bounds;
            if (editable != null)
                bounds = editable.GetBoxSelectIntersectionBoundsRelativeTo(this);
            else
                continue;

            if (bounds.Width <= 0 || bounds.Height <= 0)
                doc[i].IsSelected = false;
            else
                doc[i].IsSelected = selectionRect.Intersects(bounds);
        }
    }

    #region Clipboard and block-selection keyboard (copy as markdown, paste as blocks, backspace deletes selection)

    /// <summary>
    /// Block kinds that should replace the current block type when pasted at the start of a rich block
    /// (e.g. "# Title" must become a heading, not literal text in a Text block).
    /// </summary>
    private static bool IsStructuralBlockTypeForLineStartPaste(BlockType t) =>
        t is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 or BlockType.Heading4
        or BlockType.BulletList or BlockType.NumberedList or BlockType.Checklist
        or BlockType.Quote;

    /// <summary>Image/Divider/Equation blocks have no inline runs — merging them into a Text block drops the payload.</summary>
    private static bool PasteFirstBlockRequiresBlockInsert(BlockViewModel[] pasted) =>
        pasted.Length > 0 && pasted[0].Type is BlockType.Image or BlockType.Divider or BlockType.Equation;

    /// <summary>
    /// Applies pasted block type and body runs, then list/checklist metadata. Runs are committed first so
    /// <see cref="BlockViewModel.Type"/>'s heading path runs on the final run list.
    /// </summary>
    private static void ApplyPastedStructuralBlockToViewModel(BlockViewModel target, BlockViewModel pastedFirst)
    {
        target.CommitSpansFromEditor(InlineSpanFormatApplier.Normalize(pastedFirst.CloneSpans()));
        target.Type = pastedFirst.Type;
        if (pastedFirst.Type == BlockType.NumberedList)
            target.ListNumberIndex = pastedFirst.ListNumberIndex;
        if (pastedFirst.Type == BlockType.Checklist)
            target.IsChecked = pastedFirst.IsChecked;
    }

    /// <summary>
    /// Nested <see cref="TextBox"/> (e.g. code) uses OS copy/cut/paste and undo; we must not steal those shortcuts.
    /// Image caption uses <see cref="RichTextEditor"/> — it has no built-in undo/clipboard, so those stay on the editor.
    /// </summary>
    private bool IsFocusInsideNestedTextBox()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.FocusManager?.GetFocusedElement() is not TextBox tb)
            return false;
        return this.IsVisualAncestorOf(tb);
    }

    private bool IsImageCaptionRichEditorFocused()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.FocusManager?.GetFocusedElement() is not Visual v)
            return false;
        if (!this.IsVisualAncestorOf(v)) return false;
        var rte = v as RichTextEditor ?? v.GetVisualAncestors().OfType<RichTextEditor>().FirstOrDefault();
        return rte != null && rte.Tag is string tag && tag == "BlockEditorImageCaption";
    }

    /// <summary>Ctrl+A should select all <em>blocks</em> unless focus is in a nested text field or image caption.</summary>
    private bool ShouldDeferSelectAllBlocksShortcut() =>
        IsFocusInsideNestedTextBox() || IsImageCaptionRichEditorFocused();

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        bool ctrlF = e.Key == Key.F && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlF)
        {
            OpenFindPanel();
            TryPopulateFindQueryFromFocusedSelection();
            e.Handled = true;
            return;
        }

        if (HandleFindPanelNavigationKey(e))
            return;

        if (IsReadOnly)
            return;

        var hasBlockSelection = BlockHierarchy.EnumerateInDocumentOrder(Blocks).Any(b => b.IsSelected);
        bool deferClipboardToNestedTextBox = IsFocusInsideNestedTextBox();
        bool deferUndoRedoToNestedTextBox = IsFocusInsideNestedTextBox();

        // 1. Backspace / Delete: delete all selected blocks, or delete text selection (including cross-block)
        if ((e.Key == Key.Back || e.Key == Key.Delete) && hasBlockSelection)
        {
            DeleteSelectedBlocks();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Back && !hasBlockSelection && HasCrossBlockTextSelection())
        {
            TryDeleteTextSelection();
            e.Handled = true;
            return;
        }

        // 2. Ctrl+C: copy selected blocks (or cross-block text selection) as markdown + Mnemo JSON.
        // Mark handled immediately: clipboard writes are async and may yield; if Handled stays false,
        // routing continues and the OS / other handlers can put plain text on the clipboard and wipe our payload.
        bool ctrlC = e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlC && !deferClipboardToNestedTextBox)
        {
            e.Handled = true;
            _ = TryCopySelectionToClipboardAsync();
            return;
        }

        bool ctrlX = e.Key == Key.X && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlX && !deferClipboardToNestedTextBox)
        {
            e.Handled = true;
            _ = TryCutSelectionAsync();
            return;
        }

        // 3. Ctrl+V: paste markdown as blocks (replacing block selection when present)
        bool ctrlV = e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlV && !deferClipboardToNestedTextBox)
        {
            e.Handled = true;
            _ = TryPasteFromClipboardAsync(hasBlockSelection);
            return;
        }

        // 4. Ctrl+Z: undo
        bool ctrlZ = e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
                                     && (e.KeyModifiers & KeyModifiers.Shift) == 0;
        if (ctrlZ && !deferUndoRedoToNestedTextBox)
        {
            _ = UndoAsync();
            e.Handled = true;
            return;
        }

        // 5. Ctrl+Y / Ctrl+Shift+Z: redo
        bool ctrlY = e.Key == Key.Y && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        bool ctrlShiftZ = e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
                                          && (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if ((ctrlY || ctrlShiftZ) && !deferUndoRedoToNestedTextBox)
        {
            _ = RedoAsync();
            e.Handled = true;
            return;
        }
    }

    private void Editor_KeyDown_Bubble(object? sender, KeyEventArgs e)
    {
        if (IsReadOnly)
            return;
        if (e.Handled) return;

        // 6. Ctrl+A: select all blocks
        bool ctrlA = e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlA && !ShouldDeferSelectAllBlocksShortcut())
        {
            ClearTextSelectionInAllBlocksExcept(null);
            foreach (var block in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
                block.IsSelected = true;
            e.Handled = true;
        }
    }

    private bool HandleFindPanelNavigationKey(KeyEventArgs e)
    {
        if (!_findPanelVisible)
            return false;
        if (e.Key == Key.Escape)
        {
            CloseFindPanel(clearQuery: false);
            e.Handled = true;
            return true;
        }

        if (!IsFocusInsideFindPanel())
            return false;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
            return false;

        if (e.Key == Key.Down)
        {
            NavigateFindMatches(forward: true);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Up)
        {
            NavigateFindMatches(forward: false);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private bool IsFocusInsideFindPanel()
    {
        if (_findPanel == null)
            return false;
        var top = TopLevel.GetTopLevel(this);
        if (top?.FocusManager?.GetFocusedElement() is not Visual focused)
            return false;
        return _findPanel.IsVisualAncestorOf(focused) || ReferenceEquals(focused, _findPanel);
    }

    private void OpenFindPanel()
    {
        _overlayService ??= ((App?)Application.Current)?.Services?.GetService<IOverlayService>();
        if (_overlayService == null)
            return;

        if (_findPanelVisible && _findPanel != null && !string.IsNullOrEmpty(_findOverlayId))
        {
            AttachFindOverlayScrollHost();
            UpdateFindOverlayAnchor();
            if (!string.Equals(_findPanel.FindQueryTextBox.Text ?? string.Empty, _findQuery, StringComparison.Ordinal))
                _findPanel.FindQueryTextBox.Text = _findQuery;
            UpdateFindMatchCountText();
            Dispatcher.UIThread.Post(() =>
            {
                _findPanel.FindQueryTextBox.Focus();
                _findPanel.FindQueryTextBox.SelectAll();
            }, DispatcherPriority.Input);
            RefreshFindMatchesAndHighlights();
            return;
        }

        var panel = new NoteFindPanel { EditorHost = this };
        if (!string.IsNullOrEmpty(_findQuery))
            panel.FindQueryTextBox.Text = _findQuery;
        if (!string.IsNullOrEmpty(_replaceQuery))
            panel.ReplaceQueryTextBox.Text = _replaceQuery;

        _findPanel = panel;

        TryComputeFindOverlayAnchor(this, out var anchorX, out var anchorY);
        var options = new OverlayOptions
        {
            ShowBackdrop = false,
            CloseOnOutsideClick = false,
            CloseOnEscape = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorPosition = AnchorPosition.TopLeft,
            AnchorPointX = anchorX,
            AnchorPointY = anchorY
        };
        _findOverlayId = _overlayService.CreateOverlay(panel, options, "NoteFindPanel");
        _findPanelVisible = true;
        _replacePanelExpanded = false;
        _findCaretBeforeOpen = CaptureCaretState();
        _findNavigatedToMatch = false;
        _lastFindOverlayAnchorX = null;
        _lastFindOverlayAnchorY = null;
        ApplyReplacePanelUiState();
        UpdateFindMatchCountText();
        AttachFindOverlayScrollHost();
        Dispatcher.UIThread.Post(() =>
        {
            UpdateFindOverlayAnchor();
            panel.FindQueryTextBox.Focus();
            panel.FindQueryTextBox.SelectAll();
        }, DispatcherPriority.Loaded);
        RefreshFindMatchesAndHighlights();
    }

    private void CloseFindPanel(bool clearQuery)
    {
        var shouldRestoreCaret = _findCaretBeforeOpen != null && (!_findNavigatedToMatch || _findMatches.Count == 0);

        _findPanelVisible = false;
        _replacePanelExpanded = false;
        DetachFindOverlayScrollHost();
        _lastFindOverlayAnchorX = null;
        _lastFindOverlayAnchorY = null;

        if (!string.IsNullOrEmpty(_findOverlayId) && _overlayService != null)
        {
            _overlayService.CloseOverlay(_findOverlayId);
            _findOverlayId = null;
        }

        _findPanel = null;
        _findMatches.Clear();
        _activeFindMatchIndex = -1;
        if (clearQuery)
        {
            _findQuery = string.Empty;
            _replaceQuery = string.Empty;
        }
        var caretBeforeFind = _findCaretBeforeOpen;
        _findCaretBeforeOpen = null;
        _findNavigatedToMatch = false;

        ApplyFindHighlights();
        UpdateFindMatchCountText();
        if (shouldRestoreCaret && caretBeforeFind != null)
            ApplyCaretFocus(caretBeforeFind);
        else
            RestoreEditorFocusAfterFindPanelClose();
    }

    private void RestoreEditorFocusAfterFindPanelClose()
    {
        Dispatcher.UIThread.Post(() =>
        {
            BlockViewModel? target = null;
            if (!string.IsNullOrEmpty(_focusedBlockId))
                target = BlockHierarchy.FindById(Blocks, _focusedBlockId!);
            target ??= BlockHierarchy.FindFocused(Blocks);
            target ??= BlockHierarchy.EnumerateInDocumentOrder(Blocks).FirstOrDefault();
            if (target == null)
                return;

            if (target.IsFocused)
            {
                target.IsFocused = false;
                target.IsFocused = true;
            }
            else
            {
                target.IsFocused = true;
            }
        }, DispatcherPriority.Input);
    }

    private void ApplyReplacePanelUiState()
    {
        if (_findPanel == null)
            return;
        _findPanel.FindReplaceGrid.IsVisible = _replacePanelExpanded;
        _findPanel.FindToggleReplaceTextBlock.Text = _replacePanelExpanded ? "Replace on" : "Replace off";
        _findPanel.FindOptionsRow.IsVisible = !_replacePanelExpanded;

        var caseSensitive = _replacePanelExpanded
            ? (_findPanel.FindCaseSensitiveCheckBoxReplace.IsChecked ?? false)
            : (_findPanel.FindCaseSensitiveCheckBoxCompact.IsChecked ?? false);
        var wholeWord = _replacePanelExpanded
            ? (_findPanel.FindWholeWordCheckBoxReplace.IsChecked ?? false)
            : (_findPanel.FindWholeWordCheckBoxCompact.IsChecked ?? false);

        _isSyncingFindOptionToggles = true;
        try
        {
            _findPanel.FindCaseSensitiveCheckBoxReplace.IsChecked = caseSensitive;
            _findPanel.FindCaseSensitiveCheckBoxCompact.IsChecked = caseSensitive;
            _findPanel.FindWholeWordCheckBoxReplace.IsChecked = wholeWord;
            _findPanel.FindWholeWordCheckBoxCompact.IsChecked = wholeWord;
        }
        finally
        {
            _isSyncingFindOptionToggles = false;
        }
    }

    private void UpdateFindOverlayAnchor()
    {
        if (!_findPanelVisible || string.IsNullOrEmpty(_findOverlayId) || _overlayService == null || _findPanel == null)
            return;
        var instance = _overlayService.Overlays.FirstOrDefault(o => o.Id == _findOverlayId);
        if (instance == null)
            return;
        if (!TryComputeFindOverlayAnchor(this, out var x, out var y))
            return;
        if (_lastFindOverlayAnchorX is { } lx
            && _lastFindOverlayAnchorY is { } ly
            && Math.Abs(lx - x) < 0.5
            && Math.Abs(ly - y) < 0.5)
            return;

        _lastFindOverlayAnchorX = x;
        _lastFindOverlayAnchorY = y;
        instance.Options.AnchorPointX = x;
        instance.Options.AnchorPointY = y;
        instance.Options.AnchorPosition = AnchorPosition.TopLeft;
    }

    private void AttachFindOverlayScrollHost()
    {
        DetachFindOverlayScrollHost();
        _findAnchorScrollHost = this.FindAncestorOfType<ScrollViewer>();
        if (_findAnchorScrollHost == null)
            return;
        _findAnchorScrollSizeChangedHandler = (_, _) =>
        {
            _lastFindOverlayAnchorX = null;
            _lastFindOverlayAnchorY = null;
            UpdateFindOverlayAnchor();
        };
        _findAnchorScrollHost.SizeChanged += _findAnchorScrollSizeChangedHandler;
    }

    private void DetachFindOverlayScrollHost()
    {
        if (_findAnchorScrollHost != null && _findAnchorScrollSizeChangedHandler != null)
            _findAnchorScrollHost.SizeChanged -= _findAnchorScrollSizeChangedHandler;
        _findAnchorScrollHost = null;
        _findAnchorScrollSizeChangedHandler = null;
    }

    private static bool TryComputeFindOverlayAnchor(BlockEditor editor, out double anchorLeft, out double anchorTop)
    {
        const double padding = 8;
        anchorLeft = padding;
        anchorTop = padding;
        var top = TopLevel.GetTopLevel(editor);
        if (top == null)
            return false;

        var w = FindOverlayAnchorWidthEstimate;
        var scroll = editor.FindAncestorOfType<ScrollViewer>();
        if (scroll != null)
        {
            var tl = scroll.TranslatePoint(new Point(0, 0), top);
            if (!tl.HasValue)
                return false;
            anchorLeft = tl.Value.X + scroll.Bounds.Width - w - padding;
            anchorTop = tl.Value.Y + padding;
            return true;
        }

        var editorTl = editor.TranslatePoint(new Point(0, 0), top);
        if (!editorTl.HasValue)
            return false;
        anchorLeft = editorTl.Value.X + editor.Bounds.Width - w - padding;
        anchorTop = editorTl.Value.Y + padding;
        return true;
    }

    internal void OnNoteFindPanelFindQueryTextChanged(object? sender, TextChangedEventArgs e) =>
        FindQueryTextBox_OnTextChanged(sender, e);

    internal void OnNoteFindPanelReplaceQueryTextChanged(object? sender, TextChangedEventArgs e) =>
        ReplaceQueryTextBox_OnTextChanged(sender, e);

    internal void OnNoteFindPanelOptionChanged(object? sender, RoutedEventArgs e) =>
        FindOptionCheckBox_OnChanged(sender, e);

    internal void OnNoteFindPanelFindPreviousClick(object? sender, RoutedEventArgs e) =>
        FindPreviousButton_OnClick(sender, e);

    internal void OnNoteFindPanelFindNextClick(object? sender, RoutedEventArgs e) =>
        FindNextButton_OnClick(sender, e);

    internal void OnNoteFindPanelToggleReplaceClick(object? sender, RoutedEventArgs e) =>
        FindToggleReplaceButton_OnClick(sender, e);

    internal void OnNoteFindPanelCloseClick(object? sender, RoutedEventArgs e) =>
        CloseFindPanel(clearQuery: false);

    internal void OnNoteFindPanelReplaceCurrentClick(object? sender, RoutedEventArgs e) =>
        ReplaceCurrentButton_OnClick(sender, e);

    internal void OnNoteFindPanelReplaceAllClick(object? sender, RoutedEventArgs e) =>
        ReplaceAllButton_OnClick(sender, e);

    internal void OnNoteFindPanelFindTextKeyDown(object? sender, KeyEventArgs e) =>
        FindTextBox_OnKeyDown(sender, e);

    internal void OnNoteFindPanelReplaceTextKeyDown(object? sender, KeyEventArgs e) =>
        ReplaceTextBox_OnKeyDown(sender, e);

    private void FindQueryTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _findQuery = (sender as TextBox)?.Text ?? _findPanel?.FindQueryTextBox.Text ?? string.Empty;
        RefreshFindMatchesAndHighlights();
    }

    private void ReplaceQueryTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _replaceQuery = (sender as TextBox)?.Text ?? _findPanel?.ReplaceQueryTextBox.Text ?? string.Empty;
    }

    private void FindOptionCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_findPanel != null && !_isSyncingFindOptionToggles)
        {
            _isSyncingFindOptionToggles = true;
            try
            {
                var caseSensitive = (sender == _findPanel.FindCaseSensitiveCheckBoxReplace
                                     || sender == _findPanel.FindCaseSensitiveCheckBoxCompact)
                    ? ((sender as ToggleButton)?.IsChecked ?? false)
                    : (_findPanel.FindCaseSensitiveCheckBoxReplace.IsChecked
                       ?? _findPanel.FindCaseSensitiveCheckBoxCompact.IsChecked
                       ?? false);
                var wholeWord = (sender == _findPanel.FindWholeWordCheckBoxReplace
                                 || sender == _findPanel.FindWholeWordCheckBoxCompact)
                    ? ((sender as ToggleButton)?.IsChecked ?? false)
                    : (_findPanel.FindWholeWordCheckBoxReplace.IsChecked
                       ?? _findPanel.FindWholeWordCheckBoxCompact.IsChecked
                       ?? false);

                _findPanel.FindCaseSensitiveCheckBoxReplace.IsChecked = caseSensitive;
                _findPanel.FindCaseSensitiveCheckBoxCompact.IsChecked = caseSensitive;
                _findPanel.FindWholeWordCheckBoxReplace.IsChecked = wholeWord;
                _findPanel.FindWholeWordCheckBoxCompact.IsChecked = wholeWord;
            }
            finally
            {
                _isSyncingFindOptionToggles = false;
            }
        }

        RefreshFindMatchesAndHighlights();
    }

    private void FindPreviousButton_OnClick(object? sender, RoutedEventArgs e) => NavigateFindMatches(forward: false);

    private void FindNextButton_OnClick(object? sender, RoutedEventArgs e) => NavigateFindMatches(forward: true);

    private void FindToggleReplaceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _replacePanelExpanded = !_replacePanelExpanded;
        ApplyReplacePanelUiState();
        if (_replacePanelExpanded && _findPanel != null)
            Dispatcher.UIThread.Post(() => _findPanel.ReplaceQueryTextBox.Focus(), DispatcherPriority.Input);
    }

    private void ReplaceCurrentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ReplaceCurrentFindMatch();
    }

    private void ReplaceAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ReplaceAllFindMatches();
    }

    private void FindTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            NavigateFindMatches(forward: true);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            NavigateFindMatches(forward: false);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down)
        {
            NavigateFindMatches(forward: true);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up)
        {
            NavigateFindMatches(forward: false);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            CloseFindPanel(clearQuery: false);
            e.Handled = true;
        }
    }

    private void ReplaceTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceCurrentFindMatch();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down)
        {
            NavigateFindMatches(forward: true);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up)
        {
            NavigateFindMatches(forward: false);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            CloseFindPanel(clearQuery: false);
            e.Handled = true;
        }
    }

    private void RefreshFindMatchesAndHighlights()
    {
        RebuildFindMatches();
        ApplyFindHighlights();
        UpdateFindMatchCountText();
    }

    private void RebuildFindMatches()
    {
        var previousActive = _activeFindMatchIndex >= 0 && _activeFindMatchIndex < _findMatches.Count
            ? _findMatches[_activeFindMatchIndex]
            : (FindMatch?)null;
        _findMatches.Clear();
        _activeFindMatchIndex = -1;

        if (string.IsNullOrEmpty(_findQuery))
            return;

        foreach (var block in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            if (!IsFindSearchableBlock(block))
                continue;
            var editable = GetEditableBlockForViewModel(block);
            var text = editable?.TryGetRichTextEditor()?.Text ?? block.Content ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                continue;

            foreach (var start in FindMatchOffsets(text, _findQuery, IsFindCaseSensitive(), IsFindWholeWord()))
                _findMatches.Add(new FindMatch(block.Id, start, _findQuery.Length));
        }

        if (_findMatches.Count == 0)
            return;

        if (previousActive is { } oldActive)
        {
            var preservedIndex = _findMatches.FindIndex(m =>
                m.BlockId == oldActive.BlockId
                && m.Start == oldActive.Start
                && m.Length == oldActive.Length);
            if (preservedIndex >= 0)
            {
                _activeFindMatchIndex = preservedIndex;
                return;
            }
        }

        _activeFindMatchIndex = 0;
    }

    private void ApplyFindHighlights()
    {
        var byBlockId = _findMatches.GroupBy(m => m.BlockId).ToDictionary(g => g.Key, g => g.ToList());
        FindMatch? active = _activeFindMatchIndex >= 0 && _activeFindMatchIndex < _findMatches.Count
            ? _findMatches[_activeFindMatchIndex]
            : null;

        foreach (var block in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            var rte = GetEditableBlockForViewModel(block)?.TryGetRichTextEditor();
            if (rte == null)
                continue;

            if (!byBlockId.TryGetValue(block.Id, out var rangesForBlock))
            {
                rte.SearchHighlightRanges = Array.Empty<RichTextEditor.SearchHighlightRange>();
                rte.ActiveSearchHighlightRange = null;
                continue;
            }

            rte.SearchHighlightRanges = rangesForBlock
                .Select(m => new RichTextEditor.SearchHighlightRange(m.Start, m.Length))
                .ToArray();

            if (active is { } current && current.BlockId == block.Id)
                rte.ActiveSearchHighlightRange = new RichTextEditor.SearchHighlightRange(current.Start, current.Length);
            else
                rte.ActiveSearchHighlightRange = null;
        }
    }

    private void UpdateFindMatchCountText()
    {
        if (_findPanel?.FindMatchCountTextBlock is not { } countBlock)
            return;
        if (string.IsNullOrEmpty(_findQuery))
        {
            countBlock.Text = "0/0";
            return;
        }
        if (_findMatches.Count == 0)
        {
            countBlock.Text = "0";
            return;
        }
        countBlock.Text = $"{_activeFindMatchIndex + 1}/{_findMatches.Count}";
    }

    private void NavigateFindMatches(bool forward)
    {
        if (_findMatches.Count == 0)
        {
            UpdateFindMatchCountText();
            return;
        }

        _activeFindMatchIndex = forward
            ? (_activeFindMatchIndex + 1 + _findMatches.Count) % _findMatches.Count
            : (_activeFindMatchIndex - 1 + _findMatches.Count) % _findMatches.Count;

        _findNavigatedToMatch = true;
        FocusFindMatch(_findMatches[_activeFindMatchIndex]);
        ApplyFindHighlights();
        UpdateFindMatchCountText();
    }

    private void FocusFindMatch(FindMatch match)
    {
        var block = BlockHierarchy.FindById(Blocks, match.BlockId);
        if (block == null)
            return;
        var editable = GetEditableBlockForViewModel(block);
        if (editable == null)
            return;

        ClearTextSelectionInAllBlocksExcept(block);
        block.IsFocused = true;
        editable.ApplyTextSelection(match.Start, match.Start + match.Length);
        editable.BringIntoView();
    }

    private void ReplaceCurrentFindMatch()
    {
        if (_activeFindMatchIndex < 0 || _activeFindMatchIndex >= _findMatches.Count)
            return;
        var current = _findMatches[_activeFindMatchIndex];
        var block = BlockHierarchy.FindById(Blocks, current.BlockId);
        if (block == null)
            return;
        var editable = GetEditableBlockForViewModel(block);
        if (editable == null)
            return;

        BeginStructuralChange();
        editable.ApplyTextSelection(current.Start, current.Start + current.Length);
        if (editable.InsertTextAtCursor(_replaceQuery))
        {
            CommitStructuralChange("Replace text");
            NotifyBlocksChanged();
        }
        else
        {
            CommitStructuralChange("Replace text");
        }
        RefreshFindMatchesAndHighlights();
    }

    private void ReplaceAllFindMatches()
    {
        if (_findMatches.Count == 0)
            return;

        var matchesByBlock = _findMatches.GroupBy(m => m.BlockId).ToList();
        var didChange = false;
        BeginStructuralChange();
        foreach (var group in matchesByBlock)
        {
            var block = BlockHierarchy.FindById(Blocks, group.Key);
            if (block == null)
                continue;
            var editable = GetEditableBlockForViewModel(block);
            var rte = editable?.TryGetRichTextEditor();
            var originalFlat = rte?.Text ?? block.Content ?? string.Empty;
            if (string.IsNullOrEmpty(originalFlat))
                continue;
            var newFlat = originalFlat;

            foreach (var match in group.OrderByDescending(m => m.Start))
            {
                if (match.Start < 0 || match.Start + match.Length > newFlat.Length)
                    continue;
                newFlat = newFlat.Remove(match.Start, match.Length).Insert(match.Start, _replaceQuery);
            }

            if (string.Equals(originalFlat, newFlat, StringComparison.Ordinal))
                continue;

            var sourceRuns = rte?.Spans ?? block.Spans;
            var updatedRuns = InlineSpanFormatApplier.ApplyTextEdit(sourceRuns, originalFlat, newFlat);
            if (rte != null)
                rte.Spans = updatedRuns;
            block.CommitSpansFromEditor(updatedRuns);
            didChange = true;
        }

        if (didChange)
        {
            CommitStructuralChange("Replace all text");
            NotifyBlocksChanged();
        }
        else
        {
            CommitStructuralChange("Replace all text");
        }

        RefreshFindMatchesAndHighlights();
    }

    private bool IsFindSearchableBlock(BlockViewModel block) =>
        block.Type is not BlockType.Divider and not BlockType.Image and not BlockType.Equation and not BlockType.Code;

    private bool IsFindCaseSensitive() =>
        _findPanel != null
        && ((_findPanel.FindCaseSensitiveCheckBoxReplace.IsChecked ?? false)
            || (_findPanel.FindCaseSensitiveCheckBoxCompact.IsChecked ?? false));

    private bool IsFindWholeWord() =>
        _findPanel != null
        && ((_findPanel.FindWholeWordCheckBoxReplace.IsChecked ?? false)
            || (_findPanel.FindWholeWordCheckBoxCompact.IsChecked ?? false));

    private static IEnumerable<int> FindMatchOffsets(string text, string query, bool caseSensitive, bool wholeWord)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            yield break;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = 0;
        while (start <= text.Length - query.Length)
        {
            var idx = text.IndexOf(query, start, comparison);
            if (idx < 0)
                yield break;
            if (!wholeWord || IsWholeWordBoundary(text, idx, query.Length))
                yield return idx;
            start = idx + Math.Max(query.Length, 1);
        }
    }

    private static bool IsWholeWordBoundary(string text, int start, int length)
    {
        var beforeIsWord = start > 0 && IsWordChar(text[start - 1]);
        var afterIndex = start + length;
        var afterIsWord = afterIndex < text.Length && IsWordChar(text[afterIndex]);
        return !beforeIsWord && !afterIsWord;
    }

    private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private void TryPopulateFindQueryFromFocusedSelection()
    {
        if (!_findPanelVisible || _findPanel == null)
            return;

        BlockViewModel? focused = null;
        if (!string.IsNullOrEmpty(_focusedBlockId))
            focused = BlockHierarchy.FindById(Blocks, _focusedBlockId!);
        focused ??= BlockHierarchy.FindFocused(Blocks);
        if (focused == null)
            return;

        var editable = GetEditableBlockForViewModel(focused);
        var selected = editable?.GetSelectedText();
        if (string.IsNullOrEmpty(selected))
            return;

        if (!string.Equals(_findPanel.FindQueryTextBox.Text, selected, StringComparison.Ordinal))
            _findPanel.FindQueryTextBox.Text = selected;
        _findQuery = selected;
    }

    /// <summary>
    /// Deletes all blocks that have IsSelected, then focuses the block at the first deleted index (or the one before).
    /// </summary>
    private void DeleteSelectedBlocks()
    {
        var doc = GetDocumentOrderBlocks();
        var toRemove = doc.Where(b => b.IsSelected).ToList();
        if (toRemove.Count == 0) return;

        BeginStructuralChange();

        var topLevel = toRemove.Where(b => b.OwnerTwoColumn == null).OrderByDescending(b => Blocks.IndexOf(b)).ToList();
        foreach (var block in topLevel)
        {
            UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
            Blocks.Remove(block);
        }

        var bySplit = new Dictionary<TwoColumnBlockViewModel, List<BlockViewModel>>();
        foreach (var block in toRemove.Where(b => b.OwnerTwoColumn is TwoColumnBlockViewModel))
        {
            var tc = (TwoColumnBlockViewModel)block.OwnerTwoColumn!;
            if (!bySplit.TryGetValue(tc, out var list))
            {
                list = new List<BlockViewModel>();
                bySplit[tc] = list;
            }
            list.Add(block);
        }

        foreach (var kv in bySplit)
        {
            var tc = kv.Key;
            var parts = kv.Value;
            if (Blocks.IndexOf(tc) < 0)
                continue;

            var hasLeft = parts.Any(b => b.IsLeftColumn);
            var hasRight = parts.Any(b => !b.IsLeftColumn);

            if (hasLeft && hasRight)
            {
                var topIdx = Blocks.IndexOf(tc);
                if (topIdx < 0) continue;
                UnsubscribeFromBlock(tc, removeStoredImageIfOrphan: true);
                Blocks.RemoveAt(topIdx);
                ReorderBlocks();
                continue;
            }

            if (hasLeft && !hasRight)
            {
                var allLeft = tc.LeftColumnBlocks.Count > 0 &&
                    tc.LeftColumnBlocks.All(b => parts.Contains(b));
                if (allLeft)
                {
                    UnwrapTwoColumnPromotingFilledColumn(tc, false);
                    continue;
                }

                foreach (var block in parts.OrderByDescending(b => tc.LeftColumnBlocks.IndexOf(b)))
                {
                    if (Blocks.IndexOf(tc) < 0) break;
                    if (!tc.LeftColumnBlocks.Contains(block)) continue;
                    RemoveCellFromTwoColumnOrUnwrap(tc, block);
                }
                continue;
            }

            if (hasRight && !hasLeft)
            {
                var allRight = tc.RightColumnBlocks.Count > 0 &&
                    tc.RightColumnBlocks.All(b => parts.Contains(b));
                if (allRight)
                {
                    UnwrapTwoColumnPromotingFilledColumn(tc, true);
                    continue;
                }

                foreach (var block in parts.OrderByDescending(b => tc.RightColumnBlocks.IndexOf(b)))
                {
                    if (Blocks.IndexOf(tc) < 0) break;
                    if (!tc.RightColumnBlocks.Contains(block)) continue;
                    RemoveCellFromTwoColumnOrUnwrap(tc, block);
                }
            }
        }

        if (Blocks.Count == 0)
        {
            var defaultBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
            SubscribeToBlock(defaultBlock);
            Blocks.Add(defaultBlock);
            Dispatcher.UIThread.Post(() => defaultBlock.IsFocused = true, DispatcherPriority.Input);
        }
        else
        {
            var focusTarget = BlockHierarchy.EnumerateInDocumentOrder(Blocks).FirstOrDefault();
            if (focusTarget != null)
                Dispatcher.UIThread.Post(() => focusTarget.IsFocused = true, DispatcherPriority.Input);
        }

        ReorderBlocks();
        ClearBlockSelection();
        CommitStructuralChange("Delete selected blocks");
        BlocksChanged?.Invoke();
    }

    public bool HasCrossBlockTextSelection()
    {
        foreach (var vm in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            if (GetEditableBlockForViewModel(vm)?.GetSelectionRange() != null) return true;
        }
        return false;
    }

    /// <summary>True if any block in the cross-block text selection overlaps linked text.</summary>
    public bool CrossBlockTextSelectionHasLink()
    {
        foreach (var vm in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            var eb = GetEditableBlockForViewModel(vm);
            if (eb?.GetSelectionRange() == null) continue;
            if (eb.DoesSelectionHaveLink()) return true;
        }
        return false;
    }

    /// <summary>First <c>LinkUrl</c> found in any block that has a text selection (cross-block link edit).</summary>
    public string? TryGetFirstLinkUrlInCrossBlockSelection()
    {
        foreach (var vm in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            var eb = GetEditableBlockForViewModel(vm);
            if (eb?.GetSelectionRange() is not { } sel) continue;
            var url = GetLinkUrlInRuns(vm.Spans, sel.start, sel.end);
            if (!string.IsNullOrEmpty(url)) return url;
        }
        return null;
    }

    private static string? GetLinkUrlInRuns(IReadOnlyList<InlineSpan> runs, int start, int end)
    {
        int pos = 0;
        foreach (var seg in runs)
        {
            if (seg is not TextSpan run)
            {
                pos += 1;
                continue;
            }

            int re = pos + run.Text.Length;
            if (re > start && pos < end && run.Style.LinkUrl != null)
                return run.Style.LinkUrl;
            pos = re;
        }
        return null;
    }

    public void ApplyInlineFormatToCrossBlockSelection(Mnemo.Core.Formatting.InlineFormatKind kind, string? color = null)
    {
        BeginStructuralChange();
        _isApplyingCrossBlockFormat = true;
        try
        {
            var doc = GetDocumentOrderBlocks();
            for (int i = 0; i < doc.Count; i++)
            {
                var editableBlock = GetEditableBlockForViewModel(doc[i]);
                if (editableBlock?.GetSelectionRange() != null)
                    editableBlock.ApplyInlineFormatInternal(kind, color);
            }
        }
        finally
        {
            _isApplyingCrossBlockFormat = false;
        }

        CommitStructuralChange("Format Selection");
    }

    private void TryDeleteTextSelection()
    {
        BlockViewModel? firstAffected = null;
        foreach (var block in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            var editableBlock = GetEditableBlockForViewModel(block);
            if (editableBlock?.GetSelectionRange() != null)
            {
                firstAffected ??= block;
                editableBlock.DeleteSelection();
            }
        }

        RemoveEmptyBlocksAfterTextDelete(firstAffected);
    }

    /// <summary>
    /// Removes empty blocks after a text-delete operation, but keeps the first block that had
    /// selection (even if empty) and focuses it. Other empty blocks are removed.
    /// </summary>
    private void RemoveEmptyBlocksAfterTextDelete(BlockViewModel? firstBlockInDeletion)
    {
        BeginStructuralChange();

        var docOrder = BlockHierarchy.EnumerateInDocumentOrder(Blocks).ToList();
        var toRemove = new List<BlockViewModel>();
        foreach (var block in docOrder)
        {
            if (block.Type is BlockType.TwoColumn or BlockType.Divider)
                continue;
            if (!BlockEditorContentPolicy.IsVisuallyEmpty(block.Content))
                continue;
            if (firstBlockInDeletion != null && ReferenceEquals(block, firstBlockInDeletion))
                continue;
            toRemove.Add(block);
        }

        var toRemoveSet = new HashSet<BlockViewModel>(toRemove);
        for (int i = docOrder.Count - 1; i >= 0; i--)
        {
            var block = docOrder[i];
            if (!toRemoveSet.Contains(block)) continue;
            RemoveEmptyBlockCellForTextDelete(block);
        }

        if (Blocks.Count == 0)
        {
            var defaultBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
            SubscribeToBlock(defaultBlock);
            Blocks.Add(defaultBlock);
            Dispatcher.UIThread.Post(
                () => defaultBlock.IsFocused = true,
                DispatcherPriority.Input);
        }
        else
        {
            var focusTarget = firstBlockInDeletion != null && BlockHierarchy.FindById(Blocks, firstBlockInDeletion.Id) != null
                ? firstBlockInDeletion
                : BlockHierarchy.EnumerateInDocumentOrder(Blocks).FirstOrDefault();
            if (focusTarget != null)
            {
                Dispatcher.UIThread.Post(
                    () => focusTarget.IsFocused = true,
                    DispatcherPriority.Input);
            }
        }

        ReorderBlocks();
        ClearBlockSelection();
        CommitStructuralChange("Delete text selection");
        BlocksChanged?.Invoke();
    }

    private void RemoveEmptyBlockCellForTextDelete(BlockViewModel block)
    {
        if (block.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = block.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            var ci = col.IndexOf(block);
            if (ci < 0) return;

            if (col.Count == 1 && tc.LeftColumnBlocks.Count + tc.RightColumnBlocks.Count == 1)
            {
                var topIdx = Blocks.IndexOf(tc);
                if (topIdx < 0) return;
                UnsubscribeFromBlock(tc, removeStoredImageIfOrphan: true);
                Blocks.RemoveAt(topIdx);
                var placeholder = BlockFactory.CreateBlock(BlockType.Text, 0);
                SubscribeToBlock(placeholder);
                Blocks.Insert(topIdx, placeholder);
                ReorderBlocks();
                return;
            }

            UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
            col.RemoveAt(ci);
            BlockHierarchy.ClearChildOwnership(block);
            if (col.Count == 0)
            {
                var ph = BlockFactory.CreateBlock(BlockType.Text, 0);
                BlockHierarchy.WireChildOwnership(tc, ph, block.IsLeftColumn);
                col.Add(ph);
                SubscribeToBlock(ph);
            }
            ReorderBlocks();
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index == -1) return;

        if (Blocks.Count == 1)
        {
            block.Content = string.Empty;
            block.Type = BlockType.Text;
            return;
        }

        UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
        Blocks.Remove(block);
        ReorderBlocks();
    }

    private async Task TryCopySelectionToClipboardAsync()
    {
        await TryCopySelectionToClipboardCoreAsync();
    }

    private async Task TryCutSelectionAsync()
    {
        var copied = await TryCopySelectionToClipboardCoreAsync();
        if (!copied) return;

        var hasBlockSelection = BlockHierarchy.EnumerateInDocumentOrder(Blocks).Any(b => b.IsSelected);
        if (hasBlockSelection)
        {
            DeleteSelectedBlocks();
            return;
        }

        if (HasCrossBlockTextSelection())
        {
            TryDeleteTextSelection();
            return;
        }

        var focusVm = BlockHierarchy.FindFocused(Blocks);
        if (focusVm == null) return;
        var ed = GetEditableBlockForViewModel(focusVm);
        if (ed?.GetSelectionRange() is { } range && range.start < range.end)
            ed.DeleteSelection();
    }

    /// <returns>True if clipboard was written.</returns>
    private async Task<bool> TryCopySelectionToClipboardCoreAsync()
    {
        FlushTypingBatch();

        // Mode 2: block selection (drag-box)
        var selectedBlocks = BlockHierarchy.EnumerateInDocumentOrder(Blocks).Where(b => b.IsSelected).ToList();
        if (selectedBlocks.Count > 0)
        {
            await WriteBlocksToClipboardAsync(selectedBlocks);
            return true;
        }

        var toCopy = new List<BlockViewModel>();
        var docList = GetDocumentOrderBlocks();
        for (int i = 0; i < docList.Count; i++)
        {
            var editableBlock = GetEditableBlockForViewModel(docList[i]);
            if (editableBlock == null) continue;
            var range = editableBlock.GetSelectionRange();
            if (range == null || range.Value.start >= range.Value.end) continue;
            var block = docList[i];
            int start = range.Value.start, end = range.Value.end;
            var liveRuns = GetLiveRunsForBlock(block);
            var sliceRuns = InlineSpanFormatApplier.SliceRuns(liveRuns, start, end);
            // Slices from image captions are plain text; never emit a pseudo-Image block (would serialize as full image).
            var exportType = block.Type == BlockType.Image ? BlockType.Text : block.Type;
            var vm = BlockFactory.CreateBlock(exportType, toCopy.Count);
            vm.SetSpans(sliceRuns);
            if (block.Type == BlockType.Checklist)
                vm.IsChecked = block.IsChecked;
            if (block.Type == BlockType.NumberedList)
                vm.ListNumberIndex = block.ListNumberIndex;
            toCopy.Add(vm);
        }

        if (toCopy.Count > 0)
        {
            await WriteBlocksToClipboardAsync(toCopy);
            return true;
        }

        // Mode 3: focused block (caret-only or no cross-block selection)
        var fb = BlockHierarchy.FindFocused(Blocks);
        if (fb != null)
        {
            var b = fb;
            if (b.Type == BlockType.Image)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard == null) return false;
                SyncViewModelsFromRichEditors(new[] { b });
                await topLevel.Clipboard.SetTextAsync(b.Content ?? string.Empty).ConfigureAwait(true);
                return true;
            }
            if (b.Type != BlockType.Divider)
            {
                await WriteBlocksToClipboardAsync(new List<BlockViewModel> { b });
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<InlineSpan> GetLiveRunsForBlock(BlockViewModel block)
    {
        var rte = GetEditableBlockForViewModel(block)?.TryGetRichTextEditor();
        if (rte?.Spans != null)
            return InlineSpanFormatApplier.Normalize(new List<InlineSpan>(rte.Spans));
        return block.Spans;
    }

    /// <summary>Push live <see cref="RichTextEditor.Spans"/> into the view model so clipboard serialization matches the editor.</summary>
    private void SyncViewModelsFromRichEditors(IEnumerable<BlockViewModel> blocks)
    {
        foreach (var b in blocks)
        {
            var rte = GetEditableBlockForViewModel(b)?.TryGetRichTextEditor();
            if (rte == null) continue;
            b.SetSpans(InlineSpanFormatApplier.Normalize(new List<InlineSpan>(rte.Spans)));
        }
    }

    private async Task WriteBlocksToClipboardAsync(IReadOnlyList<BlockViewModel> blocks)
    {
        if (blocks.Count == 0) return;
        SyncViewModelsFromRichEditors(blocks);
        var markdown = BlockMarkdownSerializer.Serialize(blocks);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        Bitmap? singleImageBitmap = null;
        if (blocks.Count == 1 && blocks[0].Type == BlockType.Image)
        {
            var p = blocks[0].ImagePath;
            if (!string.IsNullOrEmpty(p) && File.Exists(p))
            {
                try
                {
                    singleImageBitmap = new Bitmap(p);
                }
                catch
                {
                    singleImageBitmap = null;
                }
            }
        }

        try
        {
            if (NoteClipboardService != null && NoteClipboardCodec != null)
            {
                var doc = NoteClipboardMapper.ToDocument(blocks);
                var json = NoteClipboardCodec.Serialize(doc);
                NoteClipboardDiagnostics.Log($"Copy {blocks.Count} block(s); md preview: {Truncate(markdown, 120)}");
                for (int bi = 0; bi < blocks.Count; bi++)
                    NoteClipboardDiagnostics.Log($"  block[{bi}] type={blocks[bi].Type} {NoteClipboardDiagnostics.SummarizeSpans(blocks[bi].Spans)}");
                await NoteClipboardService.WriteAsync(topLevel.Clipboard, markdown, json, singleImageBitmap).ConfigureAwait(true);
            }
            else if (singleImageBitmap != null)
                await topLevel.Clipboard.SetBitmapAsync(singleImageBitmap).ConfigureAwait(true);
            else
                await topLevel.Clipboard.SetTextAsync(markdown).ConfigureAwait(true);
        }
        catch (Exception)
        {
        }
        finally
        {
            singleImageBitmap?.Dispose();
        }
    }

    /// <summary>First index for inserting blocks after the focused cell: column stack after the cell, or top-level after the row.</summary>
    private static int GetPasteSiblingInsertStartIndex(BlockViewModel focusedVm, int topInsert) =>
        focusedVm.OwnerTwoColumn is TwoColumnBlockViewModel tc
        && (focusedVm.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks) is { } col
        && col.IndexOf(focusedVm) is >= 0 and var cellIdx
            ? cellIdx + 1
            : topInsert + 1;

    /// <summary>Inserts a block after the focused cell in-document: into the split column when focused is in a column, otherwise into <see cref="Blocks"/>.</summary>
    private void InsertPasteSiblingBlock(BlockViewModel focusedVm, ref int insertAt, BlockViewModel block)
    {
        if (focusedVm.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = focusedVm.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            int ci = col.IndexOf(focusedVm);
            if (ci >= 0)
            {
                col.Insert(insertAt, block);
                insertAt++;
                return;
            }
        }

        Blocks.Insert(insertAt, block);
        block.Order = insertAt;
        insertAt++;
    }

    private async Task TryPasteFromClipboardAsync(bool replaceBlockSelection)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return;

            string? text = null;
            string? mnemoJson = null;
            if (NoteClipboardService != null)
            {
                var read = await NoteClipboardService.ReadAsync(topLevel.Clipboard).ConfigureAwait(true);
                mnemoJson = read.MnemoJson;
                text = read.Text;
            }
            else
                text = await topLevel.Clipboard.TryGetTextAsync().ConfigureAwait(true);

            BlockViewModel[] pasted;
            if (NoteClipboardCodec != null &&
                !string.IsNullOrEmpty(mnemoJson) &&
                NoteClipboardCodec.TryDeserialize(mnemoJson, out var document) &&
                document != null)
            {
                NoteClipboardDiagnostics.Log($"Paste: Mnemo JSON path, blocks={document.Blocks?.Count ?? 0}");
                pasted = NoteClipboardMapper.ToViewModels(document, 0).ToArray();
            }
            else
            {
                var fromSystem = await TryPasteImageBlocksFromSystemClipboardAsync(topLevel.Clipboard, text).ConfigureAwait(true);
                if (fromSystem != null)
                {
                    NoteClipboardDiagnostics.Log($"Paste: system clipboard image / file path, blocks={fromSystem.Length}");
                    pasted = fromSystem;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(text)) return;
                    NoteClipboardDiagnostics.Log($"Paste: markdown fallback, textLen={text.Length} preview={Truncate(text, 120)}");
                    pasted = BlockMarkdownSerializer.Deserialize(text);
                }
            }

            if (pasted.Length == 0) return;

            await HydratePastedImageBlocksAsync(pasted).ConfigureAwait(true);
            if (pasted.Length > 0)
                NoteClipboardDiagnostics.Log($"Paste: first block type={pasted[0].Type} {NoteClipboardDiagnostics.SummarizeSpans(pasted[0].Spans)}");

            BeginStructuralChange();

            if (!replaceBlockSelection && pasted.Length >= 1)
            {
                var focusedVm = BlockHierarchy.FindFocused(Blocks);
                if (focusedVm != null)
                {
                    var topInsert = BlockHierarchy.GetTopLevelIndex(Blocks, focusedVm);
                    if (topInsert >= 0)
                    {
                    var canInlinePaste = focusedVm.Type != BlockType.Divider && !PasteFirstBlockRequiresBlockInsert(pasted);
                    // Image caption: only merge plain text; never turn the image into a heading/list via structural paste.
                    if (focusedVm.Type == BlockType.Image)
                        canInlinePaste = canInlinePaste && pasted.Length == 1 && pasted[0].Type == BlockType.Text;

                    if (canInlinePaste)
                    {
                        var editableBlock = GetEditableBlockForViewModel(focusedVm);
                        var range = editableBlock?.GetSelectionOrCaretRange();
                        if (range.HasValue)
                        {
                            var rtePaste = editableBlock?.TryGetRichTextEditor();
                            var content = rtePaste?.Text ?? focusedVm.Content ?? string.Empty;
                            int start = Math.Clamp(range.Value.start, 0, content.Length);
                            int end = Math.Clamp(range.Value.end, 0, content.Length);
                            string textBefore = content[0..start];
                            string textAfter = content[end..];
                            string firstContent = pasted[0].Content ?? string.Empty;

                            if (focusedVm.Type == BlockType.Code)
                            {
                                // Keep single inline paste in the same line/block.
                                if (pasted.Length == 1)
                                {
                                    focusedVm.Content = textBefore + firstContent + textAfter;
                                    editableBlock!.SetCaretIndex(textBefore.Length + firstContent.Length);
                                    ClearBlockSelection();
                                    CommitStructuralChange("Paste");
                                    BlocksChanged?.Invoke();
                                    return;
                                }

                                focusedVm.Content = textBefore + firstContent;
                                editableBlock!.SetCaretIndex(textBefore.Length + firstContent.Length);

                                int insertAtCode = GetPasteSiblingInsertStartIndex(focusedVm, topInsert);
                                for (int i = 1; i < pasted.Length; i++)
                                {
                                    var block = pasted[i];
                                    string blockContent = (block.Content ?? string.Empty) +
                                        (i == pasted.Length - 1 ? textAfter : string.Empty);
                                    block.Content = blockContent;
                                    SubscribeToBlock(block);
                                    InsertPasteSiblingBlock(focusedVm, ref insertAtCode, block);
                                }
                                ReorderBlocks();
                                ClearBlockSelection();
                                CommitStructuralChange("Paste");
                                BlocksChanged?.Invoke();
                                return;
                            }

                            var liveRunsForPaste = GetLiveRunsForBlock(focusedVm);
                            var tailRuns = InlineSpanFormatApplier.SliceRuns(liveRunsForPaste, end, content.Length);
                            var beforeRuns = InlineSpanFormatApplier.SliceRuns(liveRunsForPaste, 0, start);
                            bool promoteStructuralFirst =
                                InlineSpanFormatApplier.Flatten(beforeRuns).Length == 0
                                && IsStructuralBlockTypeForLineStartPaste(pasted[0].Type);

                            int caretAfterPaste;
                            if (promoteStructuralFirst)
                            {
                                ApplyPastedStructuralBlockToViewModel(focusedVm, pasted[0]);
                                caretAfterPaste = InlineSpanFormatApplier.Flatten(focusedVm.Spans).Length;
                            }
                            else
                            {
                                var pasteFirstRuns = pasted[0].CloneSpans();
                                var mergedFirst = (pasted.Length == 1 && pasted[0].Type == BlockType.Text)
                                    ? InlineSpanFormatApplier.Normalize(
                                        new List<InlineSpan>([..beforeRuns, ..pasteFirstRuns, ..tailRuns]))
                                    : InlineSpanFormatApplier.Normalize(
                                        new List<InlineSpan>([..beforeRuns, ..pasteFirstRuns]));
                                focusedVm.CommitSpansFromEditor(mergedFirst);
                                caretAfterPaste = start + InlineSpanFormatApplier.Flatten(pasteFirstRuns).Length;
                            }

                            editableBlock!.SetCaretIndex(caretAfterPaste);

                            if (pasted.Length == 1)
                            {
                                if (pasted[0].Type != BlockType.Text && InlineSpanFormatApplier.Flatten(tailRuns).Length > 0)
                                {
                                    var tailBlockRich = BlockFactory.CreateBlock(BlockType.Text, topInsert + 1);
                                    tailBlockRich.SetSpans(tailRuns);
                                    SubscribeToBlock(tailBlockRich);
                                    int insertAtTailRich = GetPasteSiblingInsertStartIndex(focusedVm, topInsert);
                                    InsertPasteSiblingBlock(focusedVm, ref insertAtTailRich, tailBlockRich);
                                    ReorderBlocks();
                                }
                                ClearBlockSelection();
                                CommitStructuralChange("Paste");
                                BlocksChanged?.Invoke();
                                return;
                            }

                            int insertAtRich = GetPasteSiblingInsertStartIndex(focusedVm, topInsert);
                            for (int i = 1; i < pasted.Length; i++)
                            {
                                var block = pasted[i];
                                if (i == pasted.Length - 1)
                                {
                                    var mergedLast = InlineSpanFormatApplier.Normalize(
                                        new List<InlineSpan>([..block.CloneSpans(), ..tailRuns]));
                                    block.SetSpans(mergedLast);
                                }
                                SubscribeToBlock(block);
                                InsertPasteSiblingBlock(focusedVm, ref insertAtRich, block);
                            }
                            ReorderBlocks();
                            ClearBlockSelection();
                            CommitStructuralChange("Paste");
                            BlocksChanged?.Invoke();
                            return;
                        }
                    }
                    }
                }
            }

            int insertIndex;
            if (replaceBlockSelection)
            {
                var selectedIndices = new List<int>();
                for (int i = 0; i < Blocks.Count; i++)
                    if (Blocks[i].IsSelected) selectedIndices.Add(i);
                if (selectedIndices.Count == 0)
                    insertIndex = GetFocusedBlockIndex() < 0 ? Blocks.Count : GetFocusedBlockIndex() + 1;
                else
                {
                    int firstIndex = selectedIndices.Min();
                    foreach (int i in selectedIndices.OrderByDescending(x => x))
                    {
                        var block = Blocks[i];
                        UnsubscribeFromBlock(block, removeStoredImageIfOrphan: true);
                        Blocks.RemoveAt(i);
                    }
                    insertIndex = firstIndex;
                }
            }
            else
            {
                var focusedVm = BlockHierarchy.FindFocused(Blocks);
                if (focusedVm?.OwnerTwoColumn is TwoColumnBlockViewModel)
                {
                    var insertAtInColumn = GetPasteSiblingInsertStartIndex(focusedVm, GetFocusedBlockIndex());
                    foreach (var block in pasted)
                    {
                        SubscribeToBlock(block);
                        InsertPasteSiblingBlock(focusedVm, ref insertAtInColumn, block);
                    }
                    ReorderBlocks();
                    ClearBlockSelection();
                    CommitStructuralChange("Paste");
                    BlocksChanged?.Invoke();

                    if (pasted.Length > 0)
                    {
                        var firstPasted = pasted[0];
                        Dispatcher.UIThread.Post(
                            () => firstPasted.IsFocused = true,
                            DispatcherPriority.Input);
                    }

                    return;
                }

                insertIndex = GetFocusedBlockIndex();
                if (insertIndex < 0) insertIndex = Blocks.Count;
                else insertIndex++;
            }

            foreach (var block in pasted)
            {
                SubscribeToBlock(block);
                Blocks.Insert(insertIndex, block);
                block.Order = insertIndex;
                insertIndex++;
            }
            ReorderBlocks();
            ClearBlockSelection();
            CommitStructuralChange("Paste");
            BlocksChanged?.Invoke();

            if (pasted.Length > 0)
            {
                var firstPasted = pasted[0];
                Dispatcher.UIThread.Post(
                    () => firstPasted.IsFocused = true,
                    DispatcherPriority.Input);
            }
        }
        catch (Exception)
        {
        }
    }

    private static readonly HashSet<string> ClipboardImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif"
    };

    private IImageAssetService? ResolveImageAssetService() =>
        ImageAssetService ?? (Application.Current as App)?.Services?.GetService(typeof(IImageAssetService)) as IImageAssetService;

    /// <summary>
    /// Last-block guard for clicking the area below all blocks: avoid stacking duplicate empty text blocks.
    /// Image blocks often have empty <see cref="BlockViewModel.Content"/> while the file path is on <see cref="BlockViewModel.ImagePath"/>.
    /// </summary>
    private static bool IsLastBlockEmptyForBelowBlocksAreaClick(IReadOnlyList<BlockViewModel> blocks)
    {
        if (blocks.Count == 0) return false;
        // Use the last leaf block in document order so a trailing two-column row
        // does not look "empty" just because the container itself has no Content.
        var last = BlockHierarchy.EnumerateInDocumentOrder(blocks).LastOrDefault() ?? blocks[blocks.Count - 1];
        if (last.Type == BlockType.Image)
        {
            if (!string.IsNullOrWhiteSpace(last.ImagePath)) return false;
            if (!string.IsNullOrWhiteSpace(last.Content)) return false;
        }

        return BlockEditorContentPolicy.IsVisuallyEmpty(last.Content);
    }

    private static string GetBlockMetaString(BlockViewModel vm, string key)
    {
        if (!vm.Meta.TryGetValue(key, out var val) || val == null) return string.Empty;
        if (val is string s) return s;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return val.ToString() ?? string.Empty;
    }

    private static string? NormalizePathForImageCompare(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private bool AnyOtherImageBlockUsesPath(string path, BlockViewModel excludeBlock)
    {
        var norm = NormalizePathForImageCompare(path);
        if (norm == null) return false;
        foreach (var b in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            if (ReferenceEquals(b, excludeBlock)) continue;
            if (b.Type != BlockType.Image) continue;
            var other = NormalizePathForImageCompare(b.ImagePath);
            if (other != null && string.Equals(other, norm, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void QueueDeleteOrphanImageForRemovedBlock(BlockViewModel removedBlock)
    {
        if (removedBlock.Type != BlockType.Image) return;
        var path = removedBlock.ImagePath;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!MnemoAppPaths.IsPathUnderImagesDirectory(path)) return;
        if (AnyOtherImageBlockUsesPath(path, removedBlock)) return;
        var normalizedPath = NormalizePathForImageCompare(path);
        if (normalizedPath == null) return;

        var svc = ResolveImageAssetService();
        if (svc == null) return;
        var cts = new CancellationTokenSource();
        lock (PendingOrphanImageDeletesGate)
        {
            if (PendingOrphanImageDeletes.TryGetValue(normalizedPath, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }
            PendingOrphanImageDeletes[normalizedPath] = cts;
        }
        _ = DeleteOrphanImageFileAfterDelayAsync(svc, normalizedPath, removedBlock, cts);
    }

    private async Task DeleteOrphanImageFileAfterDelayAsync(
        IImageAssetService svc,
        string normalizedPath,
        BlockViewModel removedBlock,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(OrphanImageDeleteDelay, cts.Token).ConfigureAwait(false);
            if (cts.IsCancellationRequested) return;
            if (AnyOtherImageBlockUsesPath(normalizedPath, removedBlock)) return;
            await svc.DeleteStoredFileAsync(normalizedPath, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when undo/redo or reparenting reuses the same image path.
        }
        catch
        {
            // Best-effort; avoid surfacing IO errors from background cleanup.
        }
        finally
        {
            lock (PendingOrphanImageDeletesGate)
            {
                if (PendingOrphanImageDeletes.TryGetValue(normalizedPath, out var current) && ReferenceEquals(current, cts))
                    PendingOrphanImageDeletes.Remove(normalizedPath);
            }
            cts.Dispose();
        }
    }

    private void CancelPendingOrphanImageDeletesForBlock(BlockViewModel block)
    {
        if (block.Type == BlockType.Image)
            CancelPendingOrphanImageDelete(block.ImagePath);

        if (block is not TwoColumnBlockViewModel tc) return;
        foreach (var c in tc.LeftColumnBlocks)
            CancelPendingOrphanImageDeletesForBlock(c);
        foreach (var c in tc.RightColumnBlocks)
            CancelPendingOrphanImageDeletesForBlock(c);
    }

    private void CancelPendingOrphanImageDelete(string? path)
    {
        var normalizedPath = NormalizePathForImageCompare(path);
        if (normalizedPath == null) return;
        lock (PendingOrphanImageDeletesGate)
        {
            if (!PendingOrphanImageDeletes.Remove(normalizedPath, out var cts))
                return;
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static double GetBlockMetaDouble(BlockViewModel vm, string key)
    {
        if (!vm.Meta.TryGetValue(key, out var val)) return 0;
        if (val is double d) return d;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetDouble();
        if (double.TryParse(val?.ToString(), out var p)) return p;
        return 0;
    }

    private async Task HydratePastedImageBlocksAsync(BlockViewModel[] pasted)
    {
        var svc = ResolveImageAssetService();
        if (svc == null) return;
        foreach (var vm in pasted)
        {
            if (vm.Type != BlockType.Image) continue;
            var path = vm.ImagePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(fileName, vm.Id, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var r = await svc.ImportAndCopyAsync(path, vm.Id).ConfigureAwait(true);
                if (r.IsSuccess && !string.IsNullOrEmpty(r.Value))
                    vm.ImagePath = r.Value!;
            }
            catch
            {
                // Keep original path if import fails (e.g. locked file).
            }
        }
    }

    private async Task<BlockViewModel[]?> TryPasteImageBlocksFromSystemClipboardAsync(IClipboard clipboard, string? textHint)
    {
        try
        {
            var files = await clipboard.TryGetFilesAsync().ConfigureAwait(true);
            if (files != null)
            {
                foreach (var f in files)
                {
                    var p = f.TryGetLocalPath();
                    if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                    if (!ClipboardImageExtensions.Contains(Path.GetExtension(p))) continue;
                    return new[] { CreateImageBlockStubForPaste(p) };
                }
            }
        }
        catch
        {
            // fall through
        }

        var bmp = await clipboard.TryGetBitmapAsync().ConfigureAwait(true);
        if (bmp != null)
        {
            try
            {
                return new[] { await SaveClipboardBitmapToNewImageBlockAsync(bmp).ConfigureAwait(true) };
            }
            finally
            {
                bmp.Dispose();
            }
        }

        var pathFromText = NormalizeSingleLineImagePathFromClipboard(textHint);
        if (pathFromText != null && File.Exists(pathFromText) &&
            ClipboardImageExtensions.Contains(Path.GetExtension(pathFromText)))
            return new[] { CreateImageBlockStubForPaste(pathFromText) };

        return null;
    }

    private static BlockViewModel CreateImageBlockStubForPaste(string pathOrExternal)
    {
        var vm = BlockFactory.CreateBlock(BlockType.Image, 0);
        vm.ImagePath = pathOrExternal;
        vm.ImageWidth = 0;
        vm.SetSpans(new List<InlineSpan> { InlineSpan.Plain(string.Empty) });
        return vm;
    }

    private static Task<BlockViewModel> SaveClipboardBitmapToNewImageBlockAsync(Bitmap source)
    {
        // Avalonia Bitmap / platform surface must be used on the UI thread; Task.Run breaks Save on Windows.
        var vm = BlockFactory.CreateBlock(BlockType.Image, 0);
        var dir = MnemoAppPaths.GetImagesDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, vm.Id + ".png");
        source.Save(path);
        vm.ImagePath = path;
        vm.ImageWidth = 0;
        vm.SetSpans(new List<InlineSpan> { InlineSpan.Plain(string.Empty) });
        return Task.FromResult(vm);
    }

    private static string? NormalizeSingleLineImagePathFromClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.IndexOf('\r') >= 0 || t.IndexOf('\n') >= 0) return null;
        if (t.Length >= 2 &&
            ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            t = t[1..^1].Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    /// <summary>Top-level <see cref="Blocks"/> index for the row that contains the focused leaf block.</summary>
    private int GetFocusedBlockIndex()
    {
        if (_focusedBlockIndex >= 0 && _focusedBlockIndex < Blocks.Count)
        {
            var row = Blocks[_focusedBlockIndex];
            if (row.IsFocused)
                return _focusedBlockIndex;
            if (row is TwoColumnBlockViewModel tc
                && (tc.LeftColumnBlocks.Any(b => b.IsFocused) || tc.RightColumnBlocks.Any(b => b.IsFocused)))
                return _focusedBlockIndex;
        }

        var focused = BlockHierarchy.FindFocused(Blocks);
        if (focused != null)
        {
            var top = BlockHierarchy.GetTopLevelIndex(Blocks, focused);
            if (top >= 0)
            {
                _focusedBlockIndex = top;
                return top;
            }
        }
        _focusedBlockIndex = -1;
        return -1;
    }

    #endregion

    /// <summary>Document-order leaf index at the given point (editor coordinates), or -1 if none.</summary>
    private int GetBlockIndexAtPoint(Point point)
    {
        var doc = GetDocumentOrderBlocks();
        for (int i = 0; i < doc.Count; i++)
        {
            var rect = GetControlBoundsInEditor(GetEditableBlockForViewModel(doc[i]));
            if (rect.Width > 0 && rect.Height > 0 && rect.Contains(point))
                return i;
        }
        if (doc.Count > 0)
        {
            var lastRect = GetControlBoundsInEditor(GetEditableBlockForViewModel(doc[^1]));
            if (lastRect.Height > 0 && point.Y >= lastRect.Y + lastRect.Height)
                return doc.Count;
        }
        return -1;
    }

    /// <summary>
    /// Clears text selection (SelectionStart/SelectionEnd) in every block except the one at exceptBlockIndex.
    /// Pass -1 to clear all blocks. Used so a new press or click on empty space clears previous cross-block selection.
    /// </summary>
    private void ClearTextSelectionInAllBlocksExcept(BlockViewModel? exceptBlock)
    {
        foreach (var b in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
        {
            if (exceptBlock != null && ReferenceEquals(b, exceptBlock)) continue;
            GetEditableBlockForViewModel(b)?.ApplyTextSelection(0, 0);
        }
    }

    private void UpdateCrossBlockSelection(Point currentPoint)
    {
        int anchorIndex = _crossBlockAnchorBlockIndex;
        if (anchorIndex < 0 || _crossBlockAnchorBlock == null) return;

        var docList = GetDocumentOrderBlocks();
        int rawIndex = GetBlockIndexAtPoint(currentPoint);
        if (rawIndex < 0) return;
        rawIndex = Math.Clamp(rawIndex, 0, Math.Max(0, docList.Count - 1));

        int currentIndex = rawIndex;
        if (_lastCrossBlockCurrentIndex >= 0 && _lastCrossBlockCurrentIndex < docList.Count)
        {
            var rect = GetControlBoundsInEditor(GetEditableBlockForViewModel(docList[_lastCrossBlockCurrentIndex]));
            if (rect.Width > 0 && rect.Height > 0 && rect.Contains(currentPoint))
                currentIndex = _lastCrossBlockCurrentIndex;
        }
        _lastCrossBlockCurrentIndex = currentIndex;

        int anchorChar = _crossBlockAnchorCharIndex;
        bool forward = currentIndex >= anchorIndex;
        int startIdx = Math.Min(anchorIndex, currentIndex);
        int endIdx = Math.Max(anchorIndex, currentIndex);

        // Text selection only: never use block selection (IsSelected). Clear it on all blocks.
        foreach (var b in Blocks)
            b.IsSelected = false;

        for (int i = 0; i < docList.Count; i++)
        {
            var editableBlock = GetEditableBlockForViewModel(docList[i]);
            if (editableBlock == null) continue;

            // Blocks outside the selection range must be explicitly cleared so that shrinking
            // the selection (e.g. dragging back toward the anchor) removes highlights correctly.
            if (i < startIdx || i > endIdx)
            {
                editableBlock.ApplyTextSelection(0, 0);
                continue;
            }

            var block = docList[i];
            int len = editableBlock.GetLogicalTextLengthForCrossBlockSelection();

            if (anchorIndex == currentIndex)
            {
                // Single block: select text from anchor to current point
                var ptInBlock = this.TranslatePoint(currentPoint, editableBlock);
                int curChar = ptInBlock.HasValue ? editableBlock.GetCharacterIndexFromPoint(ptInBlock.Value) : anchorChar;
                int selStart = Math.Min(anchorChar, curChar);
                int selEnd = Math.Max(anchorChar, curChar);
                editableBlock.ApplyTextSelection(selStart, selEnd);
            }
            else if (i == anchorIndex)
            {
                // Anchor block: text from anchor to end (forward) or start to anchor (backward)
                if (forward)
                    editableBlock.ApplyTextSelection(anchorChar, len);
                else
                    editableBlock.ApplyTextSelection(0, anchorChar);
            }
            else if (i == currentIndex)
            {
                // Endpoint block: text from start to current point (forward) or current point to end (backward)
                var ptInBlock = this.TranslatePoint(currentPoint, editableBlock);
                int curChar = ptInBlock.HasValue ? editableBlock.GetCharacterIndexFromPoint(ptInBlock.Value) : 0;
                curChar = Math.Clamp(curChar, 0, len);
                if (forward)
                    editableBlock.ApplyTextSelection(0, curChar);
                else
                    editableBlock.ApplyTextSelection(curChar, len);
            }
            else
            {
                // Intermediate blocks: select all text in the block
                editableBlock.ApplyTextSelection(0, len);
            }
        }
    }

    #endregion

    #region Drag-drop: magnetic gap bands (insert index from cursor Y)

    private int _currentDropInsertIndex = -1;
    private EditableBlock? _currentDropIndicatorBlock;
    private BlockViewModel? _splitDropTargetBlock;
    private bool _splitDropLeftEdge;
    private TwoColumnBlockViewModel? _columnDropTarget;
    private bool _columnDropLeft;
    private int _columnDropInsertIndex = -1;

    // Fraction of block height that acts as a "snap-to-boundary" zone.
    // Only the top/bottom portion triggers an insert-before/after decision;
    // the middle portion keeps the current indicator to prevent flicker on
    // multi-line blocks where the midpoint sits inside visible text.
    private const double SnapBandFraction = 0.25;
    private const double HorizontalDropLineHeight = 3;

    /// <summary>
    /// Called by EditableBlock on DragOver. Computes insert index from cursor Y using
    /// snap-band boundaries with hysteresis and updates the drop indicator line.
    /// </summary>
    public void HandleBlockDragOver(Point cursorPosInEditor, BlockViewModel.BlockReorderDragPayload payload)
    {
        if (payload.BlocksInDocumentOrder.Count == 0 || Blocks.Count == 0)
        {
            ClearDropIndicator();
            return;
        }

        var primary = payload.Primary;

        if (payload.BlocksInDocumentOrder.Count == 1
            && TryUpdateSplitDropIndicator(cursorPosInEditor, primary))
            return;

        _splitDropTargetBlock = null;

        if (payload.BlocksInDocumentOrder.Count == 1
            && primary.OwnerTwoColumn is TwoColumnBlockViewModel otcSnap
            && TryGetTopLevelInsertInSplitRowSnapBand(cursorPosInEditor.Y, otcSnap, out var snapInsert))
        {
            ClearDropIndicator();
            _currentDropInsertIndex = snapInsert;
            ShowHorizontalReorderDropLineInOverlay(snapInsert);
            return;
        }

        if (payload.BlocksInDocumentOrder.Count == 1
            && TryUpdateColumnDropIndicator(cursorPosInEditor, primary))
            return;

        if (_columnDropTarget != null)
            ClearDropIndicator();

        var insertIndex = GetInsertIndex(cursorPosInEditor.Y);
        if (insertIndex < 0)
        {
            ClearDropIndicator();
            return;
        }

        var sortedIndices = payload.BlocksInDocumentOrder
            .Select(b => Blocks.IndexOf(b))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();
        if (sortedIndices.Count == 0)
        {
            if (payload.BlocksInDocumentOrder.Count != 1 || primary.OwnerTwoColumn is not TwoColumnBlockViewModel otc)
            {
                ClearDropIndicator();
                return;
            }
            var ti = Blocks.IndexOf(otc);
            if (ti < 0)
            {
                ClearDropIndicator();
                return;
            }
            sortedIndices = new List<int> { ti };
        }

        int gMin = sortedIndices[0];
        int gMax = sortedIndices[^1];
        bool contiguous = sortedIndices.Count == gMax - gMin + 1;
        bool nestedDrag = payload.BlocksInDocumentOrder.Count == 1 && primary.OwnerTwoColumn != null;

        if (contiguous && insertIndex >= gMin && insertIndex <= gMax + 1 && !nestedDrag)
        {
            ClearDropIndicator();
            return;
        }

        // Single-block: suppress when dropping on the same slot as now (pair split edge cases preserved).
        if (payload.BlocksInDocumentOrder.Count == 1)
        {
            var draggedBlock = primary;
            var draggedIndex = Blocks.IndexOf(draggedBlock);
            if (draggedIndex >= 0 && insertIndex == draggedIndex)
            {
                ClearDropIndicator();
                return;
            }

            if (draggedIndex >= 0 && insertIndex == draggedIndex + 1)
            {
                var sib = draggedBlock.GetColumnSibling(Blocks);
                if (sib == null || Blocks.IndexOf(sib) == draggedIndex + 1)
                {
                    ClearDropIndicator();
                    return;
                }
            }
        }

        if (_splitDropTargetBlock == null
            && _columnDropTarget == null
            && insertIndex == _currentDropInsertIndex
            && this.FindControl<Border>("BlockReorderDropLineOverlay") is { IsVisible: true })
            return;

        ClearDropIndicator();
        _currentDropInsertIndex = insertIndex;

        ShowHorizontalReorderDropLineInOverlay(insertIndex);
    }

    /// <summary>
    /// Horizontal insert-before/after line in <see cref="BlockDragGhostOverlay"/> space (full row width, including split rows).
    /// </summary>
    private void ShowHorizontalReorderDropLineInOverlay(int insertIndex)
    {
        var overlay = this.FindControl<LayoutOverlayPanel>("BlockDragGhostOverlay");
        var line = this.FindControl<Border>("BlockReorderDropLineOverlay");
        if (overlay == null || line == null) return;

        var rowContainer = GetRowContainerForInsertIndex(insertIndex);
        if (rowContainer == null) return;

        var origin = rowContainer.TranslatePoint(new Point(0, 0), overlay);
        if (!origin.HasValue) return;

        double w = rowContainer.Bounds.Width;
        double rowH = rowContainer.Bounds.Height;
        if (w <= 0 || rowH <= 0) return;

        double y = insertIndex < Blocks.Count
            ? origin.Value.Y
            : origin.Value.Y + rowH - HorizontalDropLineHeight;

        line.Width = w;
        line.Height = HorizontalDropLineHeight;
        Canvas.SetLeft(line, origin.Value.X);
        Canvas.SetTop(line, y);
        line.IsVisible = true;
        overlay.InvalidateArrange();
    }

    /// <summary>
    /// Returns the current insert index (for drop). -1 if not over a valid region.
    /// </summary>
    public int CurrentDropInsertIndex => _currentDropInsertIndex;

    /// <summary>
    /// Called when drag leaves the editor or drop completes.
    /// </summary>
    public void ClearDropIndicator()
    {
        _splitDropTargetBlock = null;
        _columnDropTarget = null;
        _columnDropInsertIndex = -1;
        if (this.FindControl<Border>("BlockReorderDropLineOverlay") is { } dropLine)
        {
            dropLine.IsVisible = false;
            this.FindControl<LayoutOverlayPanel>("BlockDragGhostOverlay")?.InvalidateArrange();
        }
        if (_currentDropIndicatorBlock != null)
        {
            _currentDropIndicatorBlock.HideDropLine();
            _currentDropIndicatorBlock = null;
        }
        _currentDropInsertIndex = -1;
    }

    /// <summary>Begin undo group for dragging the column splitter (pointer down).</summary>
    public void BeginColumnSplitResize()
    {
        _isColumnSplitResizing = true;
        BeginStructuralChange();
    }

    /// <summary>Commit undo group after column splitter release.</summary>
    public void CommitColumnSplitResize()
    {
        _isColumnSplitResizing = false;
        CommitStructuralChange("Resize columns");
        NotifyBlocksChanged();
    }

    /// <summary>Build reorder payload: Ctrl/Meta + multi-selection moves all selected (column pairs expanded).</summary>
    internal BlockViewModel.BlockReorderDragPayload CreateBlockReorderPayload(BlockViewModel handleVm, KeyModifiers mods)
    {
        bool group = (mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (!group || !handleVm.IsSelected)
        {
            return new BlockViewModel.BlockReorderDragPayload
            {
                Primary = handleVm,
                BlocksInDocumentOrder = new[] { handleVm }
            };
        }

        var selected = BlockHierarchy.EnumerateInDocumentOrder(Blocks).Where(b => b.IsSelected).ToList();
        if (selected.Count < 2)
        {
            return new BlockViewModel.BlockReorderDragPayload
            {
                Primary = handleVm,
                BlocksInDocumentOrder = new[] { handleVm }
            };
        }

        var expanded = new HashSet<BlockViewModel>(selected);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var vm in expanded.ToList())
            {
                var sib = vm.GetColumnSibling(Blocks);
                if (sib != null && expanded.Add(sib))
                    changed = true;
            }
        }

        var docOrder = GetDocumentOrderBlocks();
        var ordered = expanded.OrderBy(b =>
        {
            var i = docOrder.IndexOf(b);
            return i < 0 ? int.MaxValue : i;
        }).ToList();
        return new BlockViewModel.BlockReorderDragPayload
        {
            Primary = handleVm,
            BlocksInDocumentOrder = ordered
        };
    }

    /// <summary>
    /// Perform the drop: split into columns, or move block(s) to CurrentDropInsertIndex.
    /// </summary>
    public bool TryPerformDrop(BlockViewModel draggedBlock) =>
        TryPerformDrop(new BlockViewModel.BlockReorderDragPayload
        {
            Primary = draggedBlock,
            BlocksInDocumentOrder = new[] { draggedBlock }
        });

    /// <summary>Reorder using payload from drag (single or multi-block).</summary>
    public bool TryPerformDrop(BlockViewModel.BlockReorderDragPayload payload)
    {
        if (_splitDropTargetBlock != null)
        {
            if (payload.BlocksInDocumentOrder.Count != 1)
            {
                ClearDropIndicator();
                return false;
            }

            var ok = TryPerformSplitDrop(payload.Primary, _splitDropTargetBlock, _splitDropLeftEdge);
            ClearDropIndicator();
            return ok;
        }

        if (_columnDropTarget != null && _columnDropInsertIndex >= 0)
        {
            if (payload.BlocksInDocumentOrder.Count != 1)
            {
                ClearDropIndicator();
                return false;
            }

            var ok = TryPerformColumnDrop(payload.Primary, _columnDropTarget, _columnDropLeft, _columnDropInsertIndex);
            ClearDropIndicator();
            return ok;
        }

        if (payload.BlocksInDocumentOrder.Count == 1)
            return TryPerformDropSingle(payload.Primary);

        return TryPerformMultiBlockReorder(payload);
    }

    private bool TryPerformMultiBlockReorder(BlockViewModel.BlockReorderDragPayload payload)
    {
        if (_currentDropInsertIndex < 0 || _currentDropInsertIndex > Blocks.Count)
            return false;

        var move = payload.BlocksInDocumentOrder.ToList();
        if (move.Count < 2)
            return false;

        var indices = new List<int>();
        foreach (var b in move)
        {
            int i = Blocks.IndexOf(b);
            if (i < 0)
                return false;
            indices.Add(i);
        }

        indices.Sort();
        if (indices.Distinct().Count() != indices.Count)
            return false;

        int insertGapOriginal = _currentDropInsertIndex;

        BeginStructuralChange();
        foreach (var idx in indices.OrderByDescending(i => i))
        {
            var vm = Blocks[idx];
            UnsubscribeFromBlock(vm, removeStoredImageIfOrphan: false);
            Blocks.RemoveAt(idx);
        }

        int removedBefore = indices.Count(i => i < insertGapOriginal);
        int newInsert = insertGapOriginal - removedBefore;
        newInsert = Math.Clamp(newInsert, 0, Blocks.Count);

        foreach (var vm in move)
        {
            SubscribeToBlock(vm);
            Blocks.Insert(newInsert++, vm);
        }

        ReorderBlocks();
        CommitStructuralChange("Move blocks");
        NotifyBlocksChanged();
        return true;
    }

    private bool TryPerformDropSingle(BlockViewModel draggedBlock)
    {
        if (_currentDropInsertIndex < 0 || _currentDropInsertIndex > Blocks.Count)
            return false;
        var draggedIndex = Blocks.IndexOf(draggedBlock);
        if (draggedIndex < 0)
        {
            if (draggedBlock.OwnerTwoColumn == null && draggedBlock.GetColumnSibling(Blocks) == null)
                return false;
            BeginStructuralChange();
            var splitAfterDetach = DetachColumnCell(draggedBlock);
            var insertT = Math.Clamp(_currentDropInsertIndex, 0, Blocks.Count);
            SubscribeToBlock(draggedBlock);
            Blocks.Insert(insertT, draggedBlock);
            ReorderBlocks();
            TryCollapseSplitAfterDragOut(splitAfterDetach);
            CommitStructuralChange("Move block");
            NotifyBlocksChanged();
            return true;
        }

        // Side-by-side pair: Blocks.Move does not unpair / adjust meta; detach then insert at target gap.
        if (draggedBlock.GetColumnSibling(Blocks) != null)
        {
            var rawInsert = _currentDropInsertIndex;
            if (rawInsert < 0 || rawInsert > Blocks.Count)
                return false;
            if (rawInsert == draggedIndex)
                return false;
            if (rawInsert == draggedIndex + 1)
            {
                var sib = draggedBlock.GetColumnSibling(Blocks);
                var sibIdx = sib != null ? Blocks.IndexOf(sib) : -1;
                if (sibIdx == draggedIndex + 1)
                    return false;
            }

            BeginStructuralChange();
            var splitAfterDetach = DetachColumnCell(draggedBlock);
            var insertAt = draggedIndex < rawInsert ? rawInsert - 1 : rawInsert;
            insertAt = Math.Clamp(insertAt, 0, Blocks.Count);
            SubscribeToBlock(draggedBlock);
            Blocks.Insert(insertAt, draggedBlock);
            ReorderBlocks();
            TryCollapseSplitAfterDragOut(splitAfterDetach);
            CommitStructuralChange("Move block");
            NotifyBlocksChanged();
            return true;
        }

        var insertIndex = Math.Min(_currentDropInsertIndex, Blocks.Count - 1);
        var targetIndex = draggedIndex < insertIndex ? insertIndex - 1 : insertIndex;

        if (draggedIndex == targetIndex) return false;

        BeginStructuralChange();
        Blocks.Move(draggedIndex, targetIndex);
        for (int i = 0; i < Blocks.Count; i++)
            Blocks[i].Order = i;
        CommitStructuralChange("Move block");
        NotifyBlocksChanged();
        return true;
    }

    private bool TryUpdateSplitDropIndicator(Point cursorPosInEditor, BlockViewModel draggedBlock)
    {
        if (!TryGetSplitDropTarget(cursorPosInEditor, draggedBlock, out var target, out var leftEdge))
            return false;

        if (ReferenceEquals(_splitDropTargetBlock, target) && _splitDropLeftEdge == leftEdge && _currentDropIndicatorBlock != null)
            return true;

        ClearDropIndicator();
        _splitDropTargetBlock = target;
        _splitDropLeftEdge = leftEdge;
        _currentDropInsertIndex = -1;

        var blockVisual = GetEditableBlockForViewModel(target!);
        if (blockVisual == null) return true;
        _currentDropIndicatorBlock = blockVisual;
        if (leftEdge)
            blockVisual.ShowDropLineAtLeft();
        else
            blockVisual.ShowDropLineAtRight();
        return true;
    }

    private bool TryUpdateColumnDropIndicator(Point cursorPosInEditor, BlockViewModel draggedBlock)
    {
        if (!TryGetColumnDropInsert(cursorPosInEditor, draggedBlock, out var tc, out var left, out var insertIdx))
            return false;

        var col = left ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
        var from = col.IndexOf(draggedBlock);
        if (from >= 0 && (insertIdx == from || insertIdx == from + 1))
            return false;

        if (ReferenceEquals(_columnDropTarget, tc) && _columnDropLeft == left && _columnDropInsertIndex == insertIdx
            && this.FindControl<Border>("BlockReorderDropLineOverlay") is { IsVisible: true })
            return true;

        ClearDropIndicator();
        _columnDropTarget = tc;
        _columnDropLeft = left;
        _columnDropInsertIndex = insertIdx;
        _currentDropInsertIndex = -1;
        ShowColumnDropLineInOverlay(tc, left, insertIdx);
        return true;
    }

    private bool TryGetColumnDropInsert(Point cursor, BlockViewModel dragged, out TwoColumnBlockViewModel tc,
        out bool leftColumn, out int insertIndex)
    {
        tc = null!;
        leftColumn = false;
        insertIndex = 0;
        if (dragged is TwoColumnBlockViewModel || dragged.Type == BlockType.Divider)
            return false;

        var containers = GetBlockContainersInOrder();
        if (containers == null) return false;
        for (int r = 0; r < BlockRows.Count && r < containers.Count; r++)
        {
            if (BlockRows[r] is not SplitBlockRowViewModel sp) continue;
            tc = sp.TwoColumn;
            var rowHost = containers[r];
            var splitView = rowHost.GetVisualDescendants().OfType<SplitBlockRowView>().FirstOrDefault();
            if (splitView == null) continue;
            var leftIc = splitView.FindControl<ItemsControl>("LeftColumnItems");
            var rightIc = splitView.FindControl<ItemsControl>("RightColumnItems");
            if (leftIc == null || rightIc == null) continue;
            var lTl = leftIc.TranslatePoint(new Point(0, 0), this);
            var rTl = rightIc.TranslatePoint(new Point(0, 0), this);
            if (!lTl.HasValue || !rTl.HasValue) continue;
            var leftRect = new Rect(lTl.Value, leftIc.Bounds.Size);
            var rightRect = new Rect(rTl.Value, rightIc.Bounds.Size);
            if (leftRect.Contains(cursor))
            {
                leftColumn = true;
                return TryInsertIndexInColumnStack(cursor.Y, tc.LeftColumnBlocks, out insertIndex);
            }
            if (rightRect.Contains(cursor))
            {
                leftColumn = false;
                return TryInsertIndexInColumnStack(cursor.Y, tc.RightColumnBlocks, out insertIndex);
            }
        }
        return false;
    }

    private bool TryInsertIndexInColumnStack(double cursorY, ObservableCollection<BlockViewModel> col,
        out int insertIndex)
    {
        insertIndex = 0;
        if (col.Count == 0)
        {
            insertIndex = 0;
            return true;
        }
        for (int i = 0; i < col.Count; i++)
        {
            var eb = GetEditableBlockForViewModel(col[i]);
            if (eb == null) continue;
            var b = GetControlBoundsInEditor(eb);
            if (b.Height <= 0) continue;
            double midY = b.Y + b.Height * 0.5;
            if (cursorY < midY)
            {
                insertIndex = i;
                return true;
            }
        }
        insertIndex = col.Count;
        return true;
    }

    private SplitBlockRowView? FindSplitRowView(TwoColumnBlockViewModel tc)
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null) return null;
        for (int r = 0; r < BlockRows.Count && r < containers.Count; r++)
        {
            if (BlockRows[r] is SplitBlockRowViewModel sp && ReferenceEquals(sp.TwoColumn, tc))
                return containers[r].GetVisualDescendants().OfType<SplitBlockRowView>().FirstOrDefault();
        }
        return null;
    }

    private void ShowColumnDropLineInOverlay(TwoColumnBlockViewModel tc, bool leftColumn, int insertIndex)
    {
        var overlay = this.FindControl<LayoutOverlayPanel>("BlockDragGhostOverlay");
        var line = this.FindControl<Border>("BlockReorderDropLineOverlay");
        if (overlay == null || line == null) return;

        var col = leftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
        double x, y, width;

        if (col.Count == 0)
        {
            var splitView = FindSplitRowView(tc);
            var ic = splitView?.FindControl<ItemsControl>(leftColumn ? "LeftColumnItems" : "RightColumnItems");
            if (ic == null) return;
            var tl = ic.TranslatePoint(new Point(0, 0), overlay);
            if (!tl.HasValue) return;
            x = tl.Value.X;
            y = tl.Value.Y;
            width = ic.Bounds.Width;
        }
        else if (insertIndex < col.Count)
        {
            var eb = GetEditableBlockForViewModel(col[insertIndex]);
            if (eb == null) return;
            var tl = eb.TranslatePoint(new Point(0, 0), overlay);
            if (!tl.HasValue) return;
            y = tl.Value.Y - 2;
            x = tl.Value.X;
            width = eb.Bounds.Width;
        }
        else
        {
            var eb = GetEditableBlockForViewModel(col[^1]);
            if (eb == null) return;
            var tl = eb.TranslatePoint(new Point(0, eb.Bounds.Height), overlay);
            if (!tl.HasValue) return;
            y = tl.Value.Y - HorizontalDropLineHeight * 0.5;
            var tl0 = eb.TranslatePoint(new Point(0, 0), overlay);
            if (!tl0.HasValue) return;
            x = tl0.Value.X;
            width = eb.Bounds.Width;
        }

        line.Width = width;
        line.Height = HorizontalDropLineHeight;
        Canvas.SetLeft(line, x);
        Canvas.SetTop(line, y);
        line.IsVisible = true;
        overlay.InvalidateArrange();
    }

    private bool TryPerformColumnDrop(BlockViewModel dragged, TwoColumnBlockViewModel tc, bool leftColumn, int insertIndex)
    {
        if (dragged is TwoColumnBlockViewModel || dragged.Type == BlockType.Divider)
            return false;
        var col = leftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;

        int fromIdx = col.IndexOf(dragged);
        bool sameList = fromIdx >= 0;
        if (sameList && (insertIndex == fromIdx || insertIndex == fromIdx + 1))
            return false;

        BeginStructuralChange();

        if (sameList)
        {
            col.RemoveAt(fromIdx);
            BlockHierarchy.ClearChildOwnership(dragged);
            int adj = insertIndex;
            if (fromIdx < insertIndex) adj--;
            adj = Math.Clamp(adj, 0, col.Count);
            BlockHierarchy.WireChildOwnership(tc, dragged, leftColumn);
            col.Insert(adj, dragged);
        }
        else
        {
            TwoColumnBlockViewModel? detachedFrom = null;
            if (dragged.OwnerTwoColumn != null)
                detachedFrom = DetachColumnCell(dragged);
            else if (dragged.GetColumnSibling(Blocks) != null)
                detachedFrom = DetachColumnCell(dragged);
            else
            {
                var ti = Blocks.IndexOf(dragged);
                if (ti >= 0)
                {
                    UnsubscribeFromBlock(dragged, removeStoredImageIfOrphan: false);
                    Blocks.RemoveAt(ti);
                }
            }

            BlockHierarchy.WireChildOwnership(tc, dragged, leftColumn);
            insertIndex = Math.Clamp(insertIndex, 0, col.Count);
            SubscribeToBlock(dragged);
            col.Insert(insertIndex, dragged);
            TryCollapseSplitAfterDragOut(detachedFrom);
        }

        ReorderBlocks();
        CommitStructuralChange("Move to column");
        NotifyBlocksChanged();
        return true;
    }

    /// <summary>Horizontal strip width (each side) for split-into-columns; only active near editor content edges.</summary>
    private static double GetSplitDropSideBandWidth(double contentWidth)
    {
        if (contentWidth <= 0) return 0;
        // ~10% of content, clamped — keeps most width for vertical reorder only.
        double band = contentWidth * 0.10;
        band = Math.Clamp(band, 52, 96);
        double maxHalf = Math.Max(0, (contentWidth - 24) * 0.5);
        return maxHalf > 0 ? Math.Min(band, maxHalf) : band;
    }

    private bool TryGetEditorBlocksContentXRange(out double left, out double right)
    {
        left = 0;
        right = 0;
        if (BlocksItemsControl == null) return false;
        var tl = BlocksItemsControl.TranslatePoint(new Point(0, 0), this);
        if (!tl.HasValue) return false;
        double w = BlocksItemsControl.Bounds.Width;
        if (w <= 0) return false;
        left = tl.Value.X;
        right = tl.Value.X + w;
        return true;
    }

    /// <returns>True if cursor is in a side band; <paramref name="leftEdge"/> = split with dragged as left column when in the left band.</returns>
    private bool TryGetSplitIntentFromEditorX(double cursorX, out bool leftEdge)
    {
        leftEdge = false;
        if (!TryGetEditorBlocksContentXRange(out var contentLeft, out var contentRight))
            return false;
        double contentW = contentRight - contentLeft;
        double band = GetSplitDropSideBandWidth(contentW);
        if (band <= 0) return false;
        if (cursorX < contentLeft + band)
        {
            leftEdge = true;
            return true;
        }
        if (cursorX > contentRight - band)
        {
            leftEdge = false;
            return true;
        }
        return false;
    }

    private bool TryGetSplitDropTarget(Point cursor, BlockViewModel dragged, out BlockViewModel? target, out bool leftEdge)
    {
        target = null;
        leftEdge = false;
        if (dragged.Type == BlockType.Divider)
            return false;
        if (!TryGetSplitIntentFromEditorX(cursor.X, out leftEdge))
            return false;

        var boundsList = GetBlockBoundsInEditorOrder();
        if (boundsList.Count == 0 || boundsList.Count != BlockRows.Count) return false;

        for (int r = 0; r < boundsList.Count; r++)
        {
            var (top, bottom) = boundsList[r];
            if (cursor.Y < top || cursor.Y >= bottom) continue;

            var row = BlockRows[r];
            if (row is SplitBlockRowViewModel splitRow)
            {
                BlockViewModel? hit = null;
                foreach (var vm in BlockHierarchy.EnumerateInDocumentOrder(new[] { splitRow.TwoColumn }))
                {
                    if (ReferenceEquals(vm, dragged) || vm.Type == BlockType.Divider) continue;
                    var rect = GetEditableBlockBoundsInEditor(GetEditableBlockForViewModel(vm));
                    if (rect.Width <= 0) continue;
                    if (cursor.Y < rect.Y || cursor.Y >= rect.Bottom) continue;
                    if (cursor.X >= rect.X && cursor.X < rect.Right)
                    {
                        hit = vm;
                        break;
                    }
                }
                if (hit == null)
                {
                    double best = double.MaxValue;
                    foreach (var vm in BlockHierarchy.EnumerateInDocumentOrder(new[] { splitRow.TwoColumn }))
                    {
                        if (ReferenceEquals(vm, dragged) || vm.Type == BlockType.Divider) continue;
                        var rect = GetEditableBlockBoundsInEditor(GetEditableBlockForViewModel(vm));
                        if (rect.Width <= 0) continue;
                        if (cursor.Y < rect.Y || cursor.Y >= rect.Bottom) continue;
                        double cx = rect.X + rect.Width * 0.5;
                        double d = Math.Abs(cursor.X - cx);
                        if (d < best)
                        {
                            best = d;
                            hit = vm;
                        }
                    }
                }
                if (hit == null) return false;
                target = hit;
                return true;
            }

            if (row is SingleBlockRowViewModel single)
            {
                var vm = single.Block;
                if (ReferenceEquals(vm, dragged) || vm.Type == BlockType.Divider) return false;

                var rect = GetEditableBlockBoundsInEditor(GetEditableBlockForViewModel(vm));
                if (rect.Width <= 0) return false;

                target = vm;
                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>Remove <paramref name="cell"/> from a split column or legacy flat pair.</summary>
    /// <returns>The <see cref="TwoColumnBlockViewModel"/> when detached from a nested split (caller should run collapse after drop completes).</returns>
    private TwoColumnBlockViewModel? DetachColumnCell(BlockViewModel cell)
    {
        if (cell.OwnerTwoColumn is TwoColumnBlockViewModel tc)
        {
            var col = cell.IsLeftColumn ? tc.LeftColumnBlocks : tc.RightColumnBlocks;
            UnsubscribeFromBlock(cell, removeStoredImageIfOrphan: false);
            col.Remove(cell);
            BlockHierarchy.ClearChildOwnership(cell);
            if (col.Count == 0)
            {
                var ph = BlockFactory.CreateBlock(BlockType.Text, 0);
                BlockHierarchy.WireChildOwnership(tc, ph, cell.IsLeftColumn);
                col.Add(ph);
                SubscribeToBlock(ph);
            }
            return tc;
        }

        var sibling = cell.GetColumnSibling(Blocks);
        if (sibling == null) return null;
        UnsubscribeFromBlock(cell, removeStoredImageIfOrphan: false);
        var idx = Blocks.IndexOf(cell);
        if (idx < 0) return null;
        Blocks.RemoveAt(idx);
        ColumnPairHelper.ClearPair(cell, sibling);
        return null;
    }

    private bool TryPerformSplitDrop(BlockViewModel dragged, BlockViewModel target, bool dropOnLeftEdge)
    {
        if (dragged.Type == BlockType.Divider || target.Type == BlockType.Divider)
            return false;
        // Only split top-level targets into a new row (multi-block columns need different UX).
        if (target.OwnerTwoColumn != null)
            return false;

        var targetIdx = Blocks.IndexOf(target);
        if (targetIdx < 0) return false;

        BeginStructuralChange();

        TwoColumnBlockViewModel? splitLeftBehind = null;
        if (dragged.OwnerTwoColumn != null || dragged.GetColumnSibling(Blocks) != null)
            splitLeftBehind = DetachColumnCell(dragged);

        targetIdx = Blocks.IndexOf(target);
        if (targetIdx < 0) return false;

        var targetSib = target.GetColumnSibling(Blocks);
        if (targetSib != null)
        {
            var tIdxBefore = Blocks.IndexOf(target);
            var sIdx = Blocks.IndexOf(targetSib);
            UnsubscribeFromBlock(targetSib, removeStoredImageIfOrphan: false);
            Blocks.RemoveAt(sIdx);
            ColumnPairHelper.ClearPair(target, targetSib);
            SubscribeToBlock(targetSib);
            var newTIdx = Blocks.IndexOf(target);
            if (sIdx < tIdxBefore)
                Blocks.Insert(newTIdx, targetSib);
            else
                Blocks.Insert(newTIdx + 1, targetSib);
            targetIdx = Blocks.IndexOf(target);
        }

        var draggedIdx = Blocks.IndexOf(dragged);
        if (draggedIdx >= 0)
        {
            UnsubscribeFromBlock(dragged, removeStoredImageIfOrphan: false);
            Blocks.RemoveAt(draggedIdx);
            if (draggedIdx < targetIdx) targetIdx--;
        }

        UnsubscribeFromBlock(target, removeStoredImageIfOrphan: false);
        Blocks.RemoveAt(targetIdx);

        var left = dropOnLeftEdge ? dragged : target;
        var right = dropOnLeftEdge ? target : dragged;

        var tc = new TwoColumnBlockViewModel(left.Order);
        BlockHierarchy.WireChildOwnership(tc, left, true);
        BlockHierarchy.WireChildOwnership(tc, right, false);
        tc.LeftColumnBlocks.Add(left);
        tc.RightColumnBlocks.Add(right);
        SubscribeToBlock(tc);
        Blocks.Insert(targetIdx, tc);

        ReorderBlocks();
        TryCollapseSplitAfterDragOut(splitLeftBehind);
        CommitStructuralChange("Split into columns");
        NotifyBlocksChanged();

        Dispatcher.UIThread.Post(() => left.IsFocused = true, DispatcherPriority.Input);
        return true;
    }

    private int GetInsertIndex(double cursorY)
    {
        var rowBounds = GetBlockBoundsInEditorOrder();
        if (rowBounds.Count == 0 || BlockRows.Count != rowBounds.Count)
            return -1;

        for (int r = 0; r < rowBounds.Count; r++)
        {
            var (top, bottom) = rowBounds[r];
            if (cursorY < top || cursorY >= bottom) continue;

            var row = BlockRows[r];
            int insertBeforeTop = row.StartBlockIndex;
            int insertAfterBottom = row.StartBlockIndex + row.BlockSpan;

            var height = bottom - top;
            var snapBand = Math.Max(4, height * SnapBandFraction);

            if (cursorY < top + snapBand)
                return insertBeforeTop;

            if (cursorY >= bottom - snapBand)
                return insertAfterBottom;

            if (_currentDropInsertIndex == insertBeforeTop || _currentDropInsertIndex == insertAfterBottom)
                return _currentDropInsertIndex;

            var midY = (top + bottom) / 2.0;
            return cursorY < midY ? insertBeforeTop : insertAfterBottom;
        }

        return Blocks.Count;
    }

    /// <summary>
    /// When dragging a block out of a split, prefer full-width horizontal gaps above/below the split row
    /// (snap bands) over in-column drop targets.
    /// </summary>
    private bool TryGetTopLevelInsertInSplitRowSnapBand(double cursorY, TwoColumnBlockViewModel tc, out int insertIndex)
    {
        insertIndex = -1;
        if (Blocks.IndexOf(tc) < 0) return false;

        var rowBounds = GetBlockBoundsInEditorOrder();
        if (rowBounds.Count == 0 || BlockRows.Count != rowBounds.Count)
            return false;

        for (int r = 0; r < rowBounds.Count; r++)
        {
            if (BlockRows[r] is not SplitBlockRowViewModel sp || !ReferenceEquals(sp.TwoColumn, tc))
                continue;

            var (top, bottom) = rowBounds[r];
            if (cursorY < top || cursorY >= bottom)
                continue;

            var row = BlockRows[r];
            int insertBeforeTop = row.StartBlockIndex;
            int insertAfterBottom = row.StartBlockIndex + row.BlockSpan;
            var height = bottom - top;
            var snapBand = Math.Max(4, height * SnapBandFraction);

            if (cursorY < top + snapBand)
            {
                insertIndex = insertBeforeTop;
                return true;
            }
            if (cursorY >= bottom - snapBand)
            {
                insertIndex = insertAfterBottom;
                return true;
            }
            return false;
        }
        return false;
    }

    private List<(double Top, double Bottom)> GetBlockBoundsInEditorOrder()
    {
        var list = new List<(double Top, double Bottom)>();
        var containers = GetBlockContainersInOrder();
        if (containers == null) return list;

        foreach (var child in containers)
        {
            var topLeft = child.TranslatePoint(new Point(0, 0), this);
            if (topLeft == null) continue;
            var h = child.Bounds.Height;
            if (double.IsNaN(h) || h <= 0) continue;
            list.Add((topLeft.Value.Y, topLeft.Value.Y + h));
        }

        return list;
    }

    private List<Control>? GetBlockContainersInOrder()
    {
        if (BlocksItemsControl == null) return null;
        // Use GetRealizedContainers and sort by index so order matches Blocks collection
        var containers = BlocksItemsControl.GetRealizedContainers()
            .Where(c => BlocksItemsControl.IndexFromContainer(c) >= 0)
            .OrderBy(c => BlocksItemsControl.IndexFromContainer(c))
            .ToList();
        return containers.Count > 0 ? containers : null;
    }

    private EditableBlock? GetEditableBlockForViewModel(BlockViewModel? vm)
    {
        if (vm == null) return null;
        var containers = GetBlockContainersInOrder();
        if (containers == null) return null;
        for (int r = 0; r < BlockRows.Count && r < containers.Count; r++)
        {
            var c = containers[r];
            var eb = c.GetVisualDescendants().OfType<EditableBlock>()
                .FirstOrDefault(e => ReferenceEquals(e.DataContext, vm));
            if (eb != null) return eb;
        }
        return null;
    }

    private Rect GetEditableBlockBoundsInEditor(EditableBlock? eb) => GetControlBoundsInEditor(eb);

    private Rect GetControlBoundsInEditor(Control? c)
    {
        if (c == null) return default;
        var tl = c.TranslatePoint(new Point(0, 0), this);
        if (!tl.HasValue) return default;
        return new Rect(tl.Value, c.Bounds.Size);
    }

    /// <summary>Items row container (ContentPresenter) for the row where horizontal insert <paramref name="insertIndex"/> applies.</summary>
    private Control? GetRowContainerForInsertIndex(int insertIndex)
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null || containers.Count == 0) return null;
        if (insertIndex >= Blocks.Count)
            return containers[^1];
        for (int r = 0; r < BlockRows.Count && r < containers.Count; r++)
        {
            var row = BlockRows[r];
            if (insertIndex >= row.StartBlockIndex && insertIndex < row.StartBlockIndex + row.BlockSpan)
                return containers[r];
        }
        return null;
    }

    #endregion

    #region History helpers

    private static string Truncate(string? s, int max = 40) =>
        s == null ? "<null>" : s.Length <= max ? $"'{s}'" : $"'{s[..max]}…'";

    private BlockSnapshot[] CaptureSnapshot()
    {
        var snapshots = new BlockSnapshot[Blocks.Count];
        for (int i = 0; i < Blocks.Count; i++)
            snapshots[i] = BlockSnapshot.From(Blocks[i].ToBlock());
        return snapshots;
    }

    private CaretState? CaptureCaretState()
    {
        var focused = BlockHierarchy.FindFocused(Blocks);
        if (focused != null)
            return CaptureCaretStateForBlock(focused);

        // Context menus (spellcheck suggestions, etc.) can temporarily move keyboard focus
        // away from the editor while still editing the last focused block.
        if (!string.IsNullOrEmpty(_focusedBlockId))
        {
            var lastFocused = BlockHierarchy.FindById(Blocks, _focusedBlockId);
            if (lastFocused != null)
                return CaptureCaretStateForBlock(lastFocused);
        }

        return null;
    }

    private CaretState CaptureCaretStateForBlock(BlockViewModel block, int fallbackPosition = 0)
    {
        var editableBlock = GetEditableBlockForViewModel(block);
        var max = Math.Max(0, block.Content?.Length ?? 0);
        var caretPos = Math.Clamp(editableBlock?.GetCaretIndex() ?? fallbackPosition, 0, max);
        return new CaretState { BlockId = block.Id, CaretPosition = caretPos };
    }

    /// <summary>
    /// Call before any structural mutation (insert/delete/merge/move/type-change/paste).
    /// Captures a snapshot of the current document state for undo.
    /// If a previous snapshot was never committed (orphaned), it is discarded.
    /// </summary>
    private void BeginStructuralChange()
    {
        if (_history == null || _isRestoringFromHistory) return;
        FlushTypingBatch();
        if (_pendingSnapshot == null)
            _pendingSnapshot = CaptureSnapshot();
        _pendingCaretBefore = CaptureCaretState();
    }

    /// <summary>
    /// Call after a structural mutation has completed. Pushes a DocumentOperation onto the undo stack.
    /// </summary>
    private void CommitStructuralChange(string description)
    {
        if (_history == null || _isRestoringFromHistory || _pendingSnapshot == null) return;

        var after = CaptureSnapshot();
        var caretAfter = CaptureCaretState();
        var before = _pendingSnapshot;
        var caretBefore = _pendingCaretBefore;
        _pendingSnapshot = null;
        _pendingCaretBefore = null;


        var op = new DocumentOperation(description, before, after, caretBefore, caretAfter, RestoreDocument);
        _history.Push(op);
    }

    /// <summary>
    /// Callback used by DocumentOperation to restore the editor to a previous state.
    /// Updates blocks in-place where possible to avoid a full UI rebuild.
    /// </summary>
    private void RestoreDocument(Block[] blocks, CaretState? caret)
    {
        _isRestoringFromHistory = true;
        try
        {
            var targetBlocks = blocks ?? Array.Empty<Block>();
            var flattened = ColumnPairHelper.ExpandLegacyTwoColumnBlocks(targetBlocks.OrderBy(b => b.Order));
            var existingById = new Dictionary<string, BlockViewModel>();
            foreach (var b in Blocks)
                existingById[b.Id] = b;

            var newList = new List<BlockViewModel>(flattened.Count);
            var usedIds = new HashSet<string>();

            foreach (var blk in flattened)
            {
                if (ColumnPairHelper.IsNestedTwoColumnBlock(blk))
                {
                    var tvm = new TwoColumnBlockViewModel(blk);
                    SubscribeToBlock(tvm);
                    newList.Add(tvm);
                    continue;
                }

                if (existingById.TryGetValue(blk.Id, out var existing) && usedIds.Add(blk.Id))
                {
                    blk.EnsureSpans();
                    existing.SetSpans(blk.Spans);
                    existing.Type = blk.Type;
                    existing.Meta = new Dictionary<string, object>(blk.Meta ?? new Dictionary<string, object>());
                    existing.Order = blk.Order;
                    newList.Add(existing);
                }
                else
                {
                    var vm = new BlockViewModel(blk);
                    SubscribeToBlock(vm);
                    newList.Add(vm);
                }
            }

            var mergeWork = new ObservableCollection<BlockViewModel>(newList);
            ColumnPairHelper.MergeConsecutiveColumnPairs(mergeWork);
            newList = mergeWork.ToList();

            // Unsubscribe from blocks that are no longer present
            foreach (var kvp in existingById)
            {
                if (!usedIds.Contains(kvp.Key))
                    UnsubscribeFromBlock(kvp.Value, removeStoredImageIfOrphan: false);
            }

            // Ensure at least one block
            if (newList.Count == 0)
            {
                var defaultBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
                SubscribeToBlock(defaultBlock);
                newList.Add(defaultBlock);
            }

            // Sync ObservableCollection in-place: remove extras, reorder, insert new
            for (int i = 0; i < newList.Count; i++)
            {
                if (i < Blocks.Count)
                {
                    if (!ReferenceEquals(Blocks[i], newList[i]))
                    {
                        var existIdx = Blocks.IndexOf(newList[i]);
                        if (existIdx >= 0)
                            Blocks.Move(existIdx, i);
                        else
                            Blocks.Insert(i, newList[i]);
                    }
                }
                else
                {
                    Blocks.Add(newList[i]);
                }
            }
            while (Blocks.Count > newList.Count)
                Blocks.RemoveAt(Blocks.Count - 1);

            _focusedBlockIndex = -1;
            UpdateListNumbers();

            ApplyCaretFocus(caret);
            BlocksChanged?.Invoke();
        }
        finally
        {
            // Defer clearing the flag so any TextChanged events fired by async
            // binding propagation still see _isRestoringFromHistory = true.
            Dispatcher.UIThread.Post(() => _isRestoringFromHistory = false, DispatcherPriority.Render);
        }
    }

    /// <summary>
    /// Callback used by TextEditOperation to restore a single block's runs.
    /// </summary>
    private void RestoreBlockRuns(string blockId, List<InlineSpan> runs, CaretState? caret)
    {
        _isRestoringFromHistory = true;
        try
        {
            var vm = BlockHierarchy.FindById(Blocks, blockId);
            if (vm != null)
            {
                vm.SetSpans(runs);
            }
            else
            {
            }

            ApplyCaretFocus(caret);
            BlocksChanged?.Invoke();
        }
        finally
        {
            Dispatcher.UIThread.Post(() => _isRestoringFromHistory = false, DispatcherPriority.Render);
        }
    }

    private void ApplyCaretFocus(CaretState? caret)
    {
        if (caret == null || string.IsNullOrEmpty(caret.BlockId)) return;

        foreach (var b in BlockHierarchy.EnumerateInDocumentOrder(Blocks))
            b.IsFocused = false;

        var caretPos = caret.CaretPosition;
        var target = BlockHierarchy.FindById(Blocks, caret.BlockId);
        if (target == null) return;
        target.PendingCaretIndex = caretPos;

        Dispatcher.UIThread.Post(() =>
        {
            var latestTarget = BlockHierarchy.FindById(Blocks, caret.BlockId);
            if (latestTarget == null) return;
            latestTarget.PendingCaretIndex = caretPos;
            latestTarget.IsFocused = true;
        }, DispatcherPriority.Input);
    }

    #endregion

    #region Typing batch (300ms idle → commit)

    /// <summary>
    /// Called by OnBlockContentChanged to start/extend a typing batch for the given block.
    /// <paramref name="previousText"/> is the text *before* this edit (from EditorStateManager).
    /// Must not be null — caller must have a valid pre-edit snapshot.
    /// </summary>
    internal void TrackTypingEdit(BlockViewModel block, string previousText, List<InlineSpan>? previousRuns = null)
    {
        if (_history == null || _isRestoringFromHistory)
        {
            return;
        }

        if (_typingBatchBlockId != null && _typingBatchBlockId != block.Id)
        {
            FlushTypingBatch();
        }

        if (_typingBatchBlockId == null)
        {
            _typingBatchBlockId = block.Id;
            
            if (previousRuns != null)
            {
                _typingBatchRunsBefore = previousRuns;
            }
            else
            {
                _typingBatchRunsBefore = block.CloneSpans();
                // Reconstruct the runs as they were *before* this edit using the previous text
                if (previousText != block.Content)
                {
                    _typingBatchRunsBefore = Core.Formatting.InlineSpanFormatApplier.ApplyTextEdit(
                        _typingBatchRunsBefore, block.Content, previousText);
                }
            }
            _typingBatchCaretBefore = CaptureCaretState()
                ?? CaptureCaretStateForBlock(block);
        }

        _typingBatchTimer?.Stop();
        _typingBatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TypingBatchIdleMs) };
        _typingBatchTimer.Tick += OnTypingBatchIdle;
        _typingBatchTimer.Start();
    }

    private void OnTypingBatchIdle(object? sender, EventArgs e)
    {
        FlushTypingBatch();
    }

    /// <summary>
    /// Flush the active typing batch into a TextEditOperation. Called on idle, Enter,
    /// merge, paste, block switch, and note switch.
    /// </summary>
    public void FlushTypingBatch()
    {
        _typingBatchTimer?.Stop();
        if (_typingBatchTimer != null)
        {
            _typingBatchTimer.Tick -= OnTypingBatchIdle;
            _typingBatchTimer = null;
        }

        if (_history == null || _typingBatchBlockId == null) return;

        var vm = BlockHierarchy.FindById(Blocks, _typingBatchBlockId);
        if (vm == null)
        {
            _typingBatchBlockId = null;
            _typingBatchRunsBefore = null;
            _typingBatchCaretBefore = null;
            return;
        }

        var runsAfter = vm.CloneSpans();
        var runsBefore = _typingBatchRunsBefore ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) };

        bool runsEqual = runsBefore.Count == runsAfter.Count && runsBefore.SequenceEqual(runsAfter);

        if (runsEqual)
        {
            _typingBatchBlockId = null;
            _typingBatchRunsBefore = null;
            _typingBatchCaretBefore = null;
            return;
        }

        var textBefore = Core.Formatting.InlineSpanFormatApplier.Flatten(runsBefore);
        var textAfter = vm.Content ?? string.Empty;

        var caretAfter = CaptureCaretStateForBlock(vm);


        var op = new TextEditOperation(
            "Typing",
            vm.Id,
            runsBefore,
            runsAfter,
            _typingBatchCaretBefore,
            caretAfter,
            RestoreBlockRuns);

        _history.Push(op);

        _typingBatchBlockId = null;
        _typingBatchRunsBefore = null;
        _typingBatchCaretBefore = null;
    }

    public async Task UndoAsync()
    {
        if (_history == null || !_history.CanUndo)
        {
            return;
        }
        FlushTypingBatch();
        await _history.UndoAsync();
    }

    public async Task RedoAsync()
    {
        if (_history == null || !_history.CanRedo)
        {
            return;
        }
        await _history.RedoAsync();
    }

    #endregion

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


