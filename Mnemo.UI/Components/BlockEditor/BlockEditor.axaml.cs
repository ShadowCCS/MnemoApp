using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

    /// <summary>Index of the currently focused block, or -1. Kept in sync by <see cref="OnBlockPropertyChanged"/>.</summary>
    private int _focusedBlockIndex = -1;

    // Drag-box block selection (Mode 2)
    private bool _isBoxSelecting;
    private bool _boxSelectArmed;   // true after pointer-down outside blocks, waiting for threshold
    private Point _boxSelectStart;   // editor space (for hit-test)
    private Point _boxSelectStartInOverlay; // overlay space (for drawing); set when arming
    private Border? _selectionBoxBorder;
    private const double BoxSelectThreshold = 6.0; // pixels to move before box appears
    /// <summary>Horizontal padding on EditorRoot; selection box Margin is relative to the padded content area.</summary>
    private const double EditorContentPaddingX = 32.0;

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
    private List<InlineRun>? _typingBatchRunsBefore;
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

    public ObservableCollection<BlockViewModel> Blocks
    {
        get => _blocks;
        set
        {
            _blocks = value;
            OnPropertyChanged();
        }
    }

    // TopLevel handlers for global pointer tracking (needed to intercept moves even when TextBox has capture)
    private TopLevel? _topLevel;
    private EventHandler<PointerEventArgs>? _globalPointerMovedHandler;
    private EventHandler<PointerReleasedEventArgs>? _globalPointerReleasedHandler;

    public BlockEditor()
    {
        InitializeComponent();
        DataContext = this;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragLeaveEvent, Editor_DragLeave, RoutingStrategies.Bubble);
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

    private void Editor_DragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave bubbles on every child-to-child transition inside the editor.
        // Only clear the indicator when the cursor has actually left the editor bounds.
        var pos = e.GetPosition(this);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (!bounds.Contains(pos))
            ClearDropIndicator();
    }

    public void LoadBlocks(Block[] blocks)
    {
        FlushTypingBatch();
        _pendingSnapshot = null;
        _pendingCaretBefore = null;
        _history?.Clear();

        // Unsubscribe from old blocks
        foreach (var block in Blocks)
        {
            UnsubscribeFromBlock(block);
        }

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
            // Use HashSet to track block IDs and prevent duplicates
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var block in blocks.Where(b => b != null).OrderBy(b => b.Order))
            {
                // Skip duplicate blocks with the same ID
                if (!string.IsNullOrEmpty(block.Id) && !seenIds.Add(block.Id))
                {
                    continue;
                }

                var viewModel = new BlockViewModel(block);
                SubscribeToBlock(viewModel);
                newBlocks.Add(viewModel);
            }
            
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

    private void SubscribeToBlock(BlockViewModel block)
    {
        block.ContentChanged += OnBlockContentChanged;
        block.PropertyChanged += OnBlockPropertyChanged;
        block.DeleteRequested += OnBlockDeleteRequested;
        block.DuplicateBlockRequested += OnDuplicateBlockRequested;
        block.NewBlockRequested += OnNewBlockRequested;
        block.NewBlockOfTypeRequested += OnNewBlockOfTypeRequested;
        block.DeleteAndFocusAboveRequested += OnDeleteAndFocusAboveRequested;
        block.FocusPreviousRequested += OnFocusPreviousRequested;
        block.FocusNextRequested += OnFocusNextRequested;
        block.MergeWithPreviousRequested += OnMergeWithPreviousRequested;
        block.StructuralChangeStarting += OnStructuralChangeStarting;
    }

    private void UnsubscribeFromBlock(BlockViewModel block)
    {
        block.ContentChanged -= OnBlockContentChanged;
        block.PropertyChanged -= OnBlockPropertyChanged;
        block.DeleteRequested -= OnBlockDeleteRequested;
        block.DuplicateBlockRequested -= OnDuplicateBlockRequested;
        block.NewBlockRequested -= OnNewBlockRequested;
        block.NewBlockOfTypeRequested -= OnNewBlockOfTypeRequested;
        block.DeleteAndFocusAboveRequested -= OnDeleteAndFocusAboveRequested;
        block.FocusPreviousRequested -= OnFocusPreviousRequested;
        block.FocusNextRequested -= OnFocusNextRequested;
        block.MergeWithPreviousRequested -= OnMergeWithPreviousRequested;
        block.StructuralChangeStarting -= OnStructuralChangeStarting;
    }

    private void OnStructuralChangeStarting()
    {
        BeginStructuralChange();
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
            var idx = Blocks.IndexOf(block);
            _focusedBlockIndex = idx;

            // During cross-block text selection the editor controls which blocks have text
            // selection. Clearing it here (triggered by IsFocused changes on each block we
            // update) would fight with UpdateCrossBlockSelection and cause flicker.
            if (_isCrossBlockSelecting || _crossBlockArmed) return;
            // When applying format to cross-block selection we focus each block in turn; don't clear other blocks' selection.
            if (_isApplyingCrossBlockFormat) return;

            ClearBlockSelection();
            if (idx >= 0)
                ClearTextSelectionInAllBlocksExcept(idx);
        }
        else if (_focusedBlockIndex >= 0 && _focusedBlockIndex < Blocks.Count && ReferenceEquals(Blocks[_focusedBlockIndex], block))
        {
            _focusedBlockIndex = -1;
        }
    }

    private void OnBlockContentChanged(BlockViewModel block)
    {
        ClearBlockSelection();
        var prev = block.PreviousContent;
        var prevRuns = block.PreviousRuns;
        block.PreviousContent = null;
        block.PreviousRuns = null;

        if (!_isRestoringFromHistory && _pendingSnapshot == null)
        {
            if (prev != null || prevRuns != null)
            {
                TrackTypingEdit(block, prev ?? block.Content, prevRuns);
            }
            else
            {
                BlockEditorLogger.Log($"ContentChanged but PreviousContent=null, skipping typing track. blockId={block.Id}");
            }
        }
        else
        {
            BlockEditorLogger.Log($"ContentChanged during restore/structural op. blockId={block.Id} restoring={_isRestoringFromHistory} pending={_pendingSnapshot != null}");
        }
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
        var index = Blocks.IndexOf(block);
        if (index <= 0) return;

        if (_pendingSnapshot == null)
            BeginStructuralChange();

        var previousBlock = Blocks[index - 1];
        var insertionPoint = previousBlock.Content?.Length ?? 0;
        previousBlock.Content = (previousBlock.Content ?? string.Empty) + (block.Content ?? string.Empty);

        UnsubscribeFromBlock(block);
        Blocks.Remove(block);

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

    private void DeleteBlock(BlockViewModel block)
    {
        var index = Blocks.IndexOf(block);
        if (index == -1) return;

        if (_pendingSnapshot == null)
            BeginStructuralChange();

        if (Blocks.Count == 1)
        {
            block.Content = string.Empty;
            block.Type = BlockType.Text;
            block.IsFocused = true;
            UpdateListNumbers();
            CommitStructuralChange("Clear block");
            return;
        }

        UnsubscribeFromBlock(block);
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

    private void OnNewBlockRequested(BlockViewModel block, string? initialContent)
    {
        if (_pendingSnapshot == null)
            BeginStructuralChange();

        var index = Blocks.IndexOf(block);
        AddBlock(BlockType.Text, index + 1, initialContent);

        var newBlockIndex = index + 1;
        if (newBlockIndex < Blocks.Count)
        {
            var newBlock = Blocks[newBlockIndex];
            Dispatcher.UIThread.Post(
                () => newBlock.IsFocused = true, 
                DispatcherPriority.Render);
        }

        CommitStructuralChange("Split block");
        BlocksChanged?.Invoke();
    }

    private void OnNewBlockOfTypeRequested(BlockViewModel block, BlockType type)
    {
        BeginStructuralChange();

        var index = Blocks.IndexOf(block);
        AddBlock(type, index + 1);

        var newBlockIndex = index + 1;
        if (newBlockIndex < Blocks.Count)
        {
            var newBlock = Blocks[newBlockIndex];
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

        var srcPath = GetBlockMetaString(block, "imagePath");
        if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) return;

        var index = Blocks.IndexOf(block);
        if (index < 0) return;

        var newVm = BlockFactory.CreateBlock(BlockType.Image, 0);
        var result = await svc.ImportAndCopyAsync(srcPath, newVm.Id).ConfigureAwait(true);
        if (!result.IsSuccess || string.IsNullOrEmpty(result.Value)) return;

        BeginStructuralChange();

        newVm.Meta["imagePath"] = result.Value;
        newVm.Meta["imageAlt"] = GetBlockMetaString(block, "imageAlt");
        newVm.Meta["imageWidth"] = GetBlockMetaDouble(block, "imageWidth");
        var imageAlign = GetBlockMetaString(block, "imageAlign");
        newVm.Meta["imageAlign"] = string.IsNullOrEmpty(imageAlign) ? "left" : imageAlign;

        SubscribeToBlock(newVm);
        Blocks.Insert(index + 1, newVm);
        ReorderBlocks();
        ClearBlockSelection();
        CommitStructuralChange("Duplicate image block");
        BlocksChanged?.Invoke();

        Dispatcher.UIThread.Post(() => newVm.IsFocused = true, DispatcherPriority.Input);
    }

    private void OnFocusPreviousRequested(BlockViewModel block)
    {
        var index = GetFocusedBlockIndex();
        if (index < 0) index = Blocks.IndexOf(block);
        if (index > 0)
        {
            block.IsFocused = false;
            var previousIndex = index - 1;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => Blocks[previousIndex].IsFocused = true,
                Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    private void OnFocusNextRequested(BlockViewModel block)
    {
        var index = GetFocusedBlockIndex();
        if (index < 0) index = Blocks.IndexOf(block);
        var nextIndex = index + 1;
        if (nextIndex < Blocks.Count)
        {
            block.IsFocused = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => Blocks[nextIndex].IsFocused = true,
                Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    private void ReorderBlocks()
    {
        bool hasNumberedLists = false;
        for (int i = 0; i < Blocks.Count; i++)
        {
            Blocks[i].Order = i;
            if (Blocks[i].Type == BlockType.NumberedList)
                hasNumberedLists = true;
        }
        
        if (hasNumberedLists)
            UpdateListNumbers();
    }

    private void UpdateListNumbers()
    {
        int listNumber = 1;
        for (int i = 0; i < Blocks.Count; i++)
        {
            if (Blocks[i].Type == BlockType.NumberedList)
            {
                // Check if this is the start of a new list (previous block is not a numbered list)
                if (i == 0 || Blocks[i - 1].Type != BlockType.NumberedList)
                {
                    listNumber = 1;
                }
                
                Blocks[i].ListNumberIndex = listNumber++;
            }
            else
            {
                // Reset counter when we encounter a non-numbered list block
                listNumber = 1;
            }
        }
    }

    public void NotifyBlocksChanged()
    {
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
        foreach (var block in Blocks)
            block.IsSelected = false;
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
        ClearTextSelectionInAllBlocksExcept(-1);
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
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Double/triple clicks: clear all blocks so only the block under the tap will get word/line selection from TextBox.
        if (e.ClickCount > 1)
        {
            var pt = e.GetPosition(this);
            if (IsPointInsideAnyBlock(pt))
            {
                ClearBlockSelection();
                ClearTextSelectionInAllBlocksExcept(-1);
            }
            return;
        }

        // If the press originated on the drag handle, let it propagate so DoDragDrop can run.
        var source = e.Source as Visual;
        bool hitIsDragHandle = source != null &&
            (source is Border { Tag: "DragHandle" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is "DragHandle"));
        if (hitIsDragHandle) return;

        // Image block width resize strip — must not capture here or the first drag is eaten by cross-block selection.
        bool hitIsImageResizeHandle = source != null &&
            (source is Border { Tag: "ImageResizeHandle" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is "ImageResizeHandle"));
        if (hitIsImageResizeHandle) return;

        // Let native CheckBox handling run (checklist toggles) instead of capturing in editor tunnel.
        bool hitIsCheckBox = source is CheckBox || (source != null && source.GetVisualAncestors().Any(a => a is CheckBox));
        if (hitIsCheckBox) return;

        var pos = e.GetPosition(this);

        if (IsPointInsideAnyBlock(pos))
        {
            // Press inside a block: arm cross-block selection and capture so we get PointerMoved
            var blockIndex = GetBlockIndexAtPoint(pos);
            if (blockIndex < 0 || blockIndex >= Blocks.Count) return;

            var editableBlock = GetEditableBlockAt(blockIndex);
            if (editableBlock == null) return;

            var vm = Blocks[blockIndex];

            // Image blocks have no RichTextEditor; the tunnel's capture+Handled would eat the first
            // press on toolbar Buttons/Flyouts (and caption) while focusing the block.
            if (vm.Type == BlockType.Image && !vm.IsFocused)
            {
                ClearBlockSelection();
                ClearTextSelectionInAllBlocksExcept(-1);
                vm.IsFocused = true;
                return;
            }

            // If this block already has focus, let the event reach RichTextEditor so it can set
            // the caret from the click (HitTestPoint). Otherwise we'd set Handled and the caret would never move.
            if (vm.IsFocused)
            {
                ClearTextSelectionInAllBlocksExcept(-1);
                ClearBlockSelection();

                // If the press landed outside the RichTextEditor itself (e.g. in block padding),
                // the editor won't receive OnPointerPressed. Initiate drag-select manually so the
                // full block width acts as a hit target for starting text selection.
                var richEditor = editableBlock.TryGetRichTextEditor();
                if (richEditor != null)
                {
                    bool pointerIsOverEditor = richEditor.IsPointerOver;
                    if (!pointerIsOverEditor)
                    {
                        var pointInFocusedBlock = this.TranslatePoint(pos, editableBlock);
                        if (pointInFocusedBlock.HasValue)
                        {
                            var paddingCharIndex = editableBlock.GetCharacterIndexFromPoint(pointInFocusedBlock.Value);
                            paddingCharIndex = Math.Clamp(paddingCharIndex, 0, vm.Content?.Length ?? 0);
                            richEditor.StartDragSelect(paddingCharIndex, e.Pointer);
                            e.Handled = true;
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
            _crossBlockAnchorCharIndex = Math.Clamp(charIndex, 0, vm.Content?.Length ?? 0);
            _crossBlockArmed = true;
            _isCrossBlockSelecting = false;

            // Clear all blocks first so the other block's selection always breaks; then set this block's caret.
            ClearTextSelectionInAllBlocksExcept(-1);
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
        ClearTextSelectionInAllBlocksExcept(-1);
        e.Pointer.Capture(this);
    }

    /// <summary>
    /// Returns true if the point (in editor coordinates) falls within a block's interactive hit surface
    /// (for image blocks: handle + content width, not full-row gutters beside a narrow image).
    /// </summary>
    private bool IsPointInsideAnyBlock(Point point)
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null) return false;
        for (var i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            var topLeft = container.TranslatePoint(new Point(0, 0), this);
            if (topLeft == null) continue;
            var bounds = new Rect(topLeft.Value.X, topLeft.Value.Y, container.Bounds.Width, container.Bounds.Height);
            if (!bounds.Contains(point)) continue;

            var editable = GetEditableBlockAt(i);
            if (editable == null)
                return true;

            var local = this.TranslatePoint(point, editable);
            if (!local.HasValue) continue;
            if (editable.IsPointerHitInsideBlock(local.Value))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Bubble: clear selection; arm cross-block text selection when press is inside a TextBox.
    /// Skipped when we already armed and captured in Tunnel (cross-block).
    /// </summary>
    private void Editor_PointerPressedBubble(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (_boxSelectArmed) return;
        // Already armed and captured in Tunnel for cross-block selection
        if (_crossBlockArmed) return;

        ClearBlockSelection();

        var source = e.Source as Visual;
        bool hitIsInTextBox = source is TextBox || source is RichTextEditor || (source != null && source.GetVisualAncestors().Any(a => a is TextBox || a is RichTextEditor));
        if (!hitIsInTextBox || source == null) return;

        var textBox = source as TextBox ?? source.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        var richTextEditor = source as RichTextEditor ?? source.GetVisualAncestors().OfType<RichTextEditor>().FirstOrDefault();
        var editableBlock = source as EditableBlock ?? source.GetVisualAncestors().OfType<EditableBlock>().FirstOrDefault();
        if ((textBox == null && richTextEditor == null) || editableBlock == null || editableBlock.DataContext is not BlockViewModel vm || !Blocks.Contains(vm))
            return;

        int caretIndex = textBox?.CaretIndex ?? richTextEditor?.CaretIndex ?? 0;
        int textLength = textBox?.Text?.Length ?? richTextEditor?.TextLength ?? 0;

        // Arm cross-block select — capture happens in PointerMoved once drag leaves the source block
        _crossBlockAnchorBlock = vm;
        _crossBlockAnchorBlockIndex = Blocks.IndexOf(vm);
        _crossBlockAnchorCharIndex = Math.Clamp(caretIndex, 0, textLength);
        _crossBlockStartPoint = e.GetPosition(this);
        _crossBlockArmed = true;
        _isCrossBlockSelecting = false;
        // Do NOT capture or mark handled here — let the TextBox handle its own click normally
    }

    private void Editor_PointerMoved(object? sender, PointerEventArgs e)
    {
        // Only process if at least one selection mode is armed or active
        if (!_boxSelectArmed && !_isBoxSelecting && !_crossBlockArmed && !_isCrossBlockSelecting)
            return;

        // Convert position from the event source to editor coordinates
        var current = e.GetPosition(this);

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
            _crossBlockArmed = false;
            _isCrossBlockSelecting = true;
        }

        if (_isCrossBlockSelecting && _crossBlockAnchorBlock != null)
        {
            UpdateCrossBlockSelection(current);
            e.Handled = true;
        }
    }

    private void Editor_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
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
                        var lastIsEmpty = Blocks.Count > 0 && string.IsNullOrWhiteSpace(Blocks[Blocks.Count - 1].Content);
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
                    var anchorBlock = GetEditableBlockAt(anchorIndex);
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

        var containers = GetBlockContainersInOrder();
        if (containers == null) return;

        for (int i = 0; i < containers.Count && i < Blocks.Count; i++)
        {
            var editable = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
            Rect bounds;
            if (editable != null)
                bounds = editable.GetBoxSelectIntersectionBoundsRelativeTo(this);
            else
            {
                var container = containers[i];
                var topLeft = container.TranslatePoint(new Point(0, 0), this);
                if (topLeft == null) continue;
                bounds = new Rect(topLeft.Value.X, topLeft.Value.Y, container.Bounds.Width, container.Bounds.Height);
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
                Blocks[i].IsSelected = false;
            else
                Blocks[i].IsSelected = selectionRect.Intersects(bounds);
        }
    }

    #region Clipboard and block-selection keyboard (copy as markdown, paste as blocks, backspace deletes selection)

    /// <summary>
    /// Block kinds that should replace the current block type when pasted at the start of a rich block
    /// (e.g. "# Title" must become a heading, not literal text in a Text block).
    /// </summary>
    private static bool IsStructuralBlockTypeForLineStartPaste(BlockType t) =>
        t is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3
        or BlockType.BulletList or BlockType.NumberedList or BlockType.Checklist
        or BlockType.Quote;

    /// <summary>Image/Divider blocks have no inline runs — merging them into a Text block drops the payload.</summary>
    private static bool PasteFirstBlockRequiresBlockInsert(BlockViewModel[] pasted) =>
        pasted.Length > 0 && pasted[0].Type is BlockType.Image or BlockType.Divider;

    /// <summary>
    /// Applies pasted block type and body runs, then list/checklist metadata. Runs are committed first so
    /// <see cref="BlockViewModel.Type"/>'s heading path runs on the final run list.
    /// </summary>
    private static void ApplyPastedStructuralBlockToViewModel(BlockViewModel target, BlockViewModel pastedFirst)
    {
        target.CommitRunsFromEditor(InlineRunFormatApplier.Normalize(pastedFirst.CloneRuns()));
        target.Type = pastedFirst.Type;
        if (pastedFirst.Type == BlockType.NumberedList)
            target.ListNumberIndex = pastedFirst.ListNumberIndex;
        if (pastedFirst.Type == BlockType.Checklist)
            target.IsChecked = pastedFirst.IsChecked;
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        var hasBlockSelection = Blocks.Any(b => b.IsSelected);

        // 1. Backspace: delete all selected blocks, or delete text selection (including cross-block)
        if (e.Key == Key.Back && hasBlockSelection)
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
        if (ctrlC)
        {
            e.Handled = true;
            _ = TryCopySelectionToClipboardAsync();
            return;
        }

        bool ctrlX = e.Key == Key.X && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlX)
        {
            e.Handled = true;
            _ = TryCutSelectionAsync();
            return;
        }

        // 3. Ctrl+V: paste markdown as blocks (replacing block selection when present)
        bool ctrlV = e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlV)
        {
            e.Handled = true;
            _ = TryPasteFromClipboardAsync(hasBlockSelection);
            return;
        }

        // 4. Ctrl+Z: undo
        bool ctrlZ = e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
                                     && (e.KeyModifiers & KeyModifiers.Shift) == 0;
        if (ctrlZ)
        {
            _ = UndoAsync();
            e.Handled = true;
            return;
        }

        // 5. Ctrl+Y / Ctrl+Shift+Z: redo
        bool ctrlY = e.Key == Key.Y && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        bool ctrlShiftZ = e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
                                          && (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (ctrlY || ctrlShiftZ)
        {
            _ = RedoAsync();
            e.Handled = true;
            return;
        }
    }

    private void Editor_KeyDown_Bubble(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        // 6. Ctrl+A: select all blocks
        bool ctrlA = e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlA)
        {
            ClearTextSelectionInAllBlocksExcept(-1);
            foreach (var block in Blocks)
            {
                block.IsSelected = true;
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Deletes all blocks that have IsSelected, then focuses the block at the first deleted index (or the one before).
    /// </summary>
    private void DeleteSelectedBlocks()
    {
        var selectedIndices = new List<int>();
        for (int i = 0; i < Blocks.Count; i++)
        {
            if (Blocks[i].IsSelected)
                selectedIndices.Add(i);
        }
        if (selectedIndices.Count == 0) return;

        BeginStructuralChange();

        int firstIndex = selectedIndices.Min();
        var toRemove = selectedIndices.OrderByDescending(x => x).ToList();
        foreach (int i in toRemove)
        {
            var block = Blocks[i];
            UnsubscribeFromBlock(block);
            Blocks.RemoveAt(i);
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
            int focusIndex = Math.Min(firstIndex, Blocks.Count - 1);
            if (focusIndex < 0) focusIndex = 0;
            var target = Blocks[focusIndex];
            Dispatcher.UIThread.Post(
                () => target.IsFocused = true,
                DispatcherPriority.Input);
        }

        ReorderBlocks();
        ClearBlockSelection();
        CommitStructuralChange("Delete selected blocks");
        BlocksChanged?.Invoke();
    }

    public bool HasCrossBlockTextSelection()
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null) return false;
        for (int i = 0; i < Blocks.Count && i < containers.Count; i++)
        {
            var editableBlock = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
            if (editableBlock?.GetSelectionRange() != null) return true;
        }
        return false;
    }

    public void ApplyInlineFormatToCrossBlockSelection(Mnemo.Core.Formatting.InlineFormatKind kind, string? color = null)
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null) return;

        BeginStructuralChange();
        _isApplyingCrossBlockFormat = true;
        try
        {
            for (int i = 0; i < Blocks.Count && i < containers.Count; i++)
            {
                var editableBlock = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
                if (editableBlock?.GetSelectionRange() != null)
                {
                    editableBlock.ApplyInlineFormatInternal(kind, color);
                }
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
        var containers = GetBlockContainersInOrder();
        if (containers == null) return;

        int? firstAffectedIndex = null;
        for (int i = 0; i < Blocks.Count && i < containers.Count; i++)
        {
            var editableBlock = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
            if (editableBlock?.GetSelectionRange() != null)
            {
                if (firstAffectedIndex == null) firstAffectedIndex = i;
                editableBlock.DeleteSelection();
            }
        }

        RemoveEmptyBlocksAfterTextDelete(firstAffectedIndex ?? 0);
    }

    /// <summary>
    /// Removes empty blocks after a text-delete operation, but keeps the first block that had
    /// selection (even if empty) and focuses it. Other empty blocks are removed.
    /// </summary>
    private void RemoveEmptyBlocksAfterTextDelete(int firstBlockInDeletion)
    {
        BeginStructuralChange();

        var emptyIndices = new List<int>();
        for (int i = 0; i < Blocks.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(Blocks[i].Content ?? string.Empty))
                emptyIndices.Add(i);
        }
        var toRemove = emptyIndices.Where(i => i != firstBlockInDeletion).ToList();

        foreach (int i in toRemove.OrderByDescending(x => x))
        {
            var block = Blocks[i];
            UnsubscribeFromBlock(block);
            Blocks.RemoveAt(i);
        }

        int newFocusIndex = firstBlockInDeletion - toRemove.Count(i => i < firstBlockInDeletion);
        newFocusIndex = Math.Clamp(newFocusIndex, 0, Math.Max(0, Blocks.Count - 1));

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
            var target = Blocks[newFocusIndex];
            Dispatcher.UIThread.Post(
                () => target.IsFocused = true,
                DispatcherPriority.Input);
        }

        ReorderBlocks();
        ClearBlockSelection();
        CommitStructuralChange("Delete text selection");
        BlocksChanged?.Invoke();
    }

    private async Task TryCopySelectionToClipboardAsync()
    {
        await TryCopySelectionToClipboardCoreAsync();
    }

    private async Task TryCutSelectionAsync()
    {
        var copied = await TryCopySelectionToClipboardCoreAsync();
        if (!copied) return;

        var hasBlockSelection = Blocks.Any(b => b.IsSelected);
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

        int fi = GetFocusedBlockIndex();
        if (fi < 0) return;
        var ed = GetEditableBlockAt(fi);
        if (ed?.GetSelectionRange() is { } range && range.start < range.end)
            ed.DeleteSelection();
    }

    /// <returns>True if clipboard was written.</returns>
    private async Task<bool> TryCopySelectionToClipboardCoreAsync()
    {
        FlushTypingBatch();

        // Mode 2: block selection (drag-box)
        var selectedBlocks = Blocks.Where(b => b.IsSelected).ToList();
        if (selectedBlocks.Count > 0)
        {
            await WriteBlocksToClipboardAsync(selectedBlocks);
            return true;
        }

        // Mode 1: cross-block text selection (gather blocks that have text selection)
        var containers = GetBlockContainersInOrder();
        var toCopy = new List<BlockViewModel>();
        if (containers != null)
        {
            for (int i = 0; i < Blocks.Count && i < containers.Count; i++)
            {
                var editableBlock = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
                if (editableBlock == null) continue;
                var range = editableBlock.GetSelectionRange();
                if (range == null || range.Value.start >= range.Value.end) continue;
                var block = Blocks[i];
                int start = range.Value.start, end = range.Value.end;
                var liveRuns = GetLiveRunsForBlockIndex(i);
                var sliceRuns = InlineRunFormatApplier.SliceRuns(liveRuns, start, end);
                var vm = BlockFactory.CreateBlock(block.Type, toCopy.Count);
                vm.SetRuns(sliceRuns);
                if (block.Type == BlockType.Checklist)
                    vm.IsChecked = block.IsChecked;
                if (block.Type == BlockType.NumberedList)
                    vm.ListNumberIndex = block.ListNumberIndex;
                toCopy.Add(vm);
            }
        }

        if (toCopy.Count > 0)
        {
            await WriteBlocksToClipboardAsync(toCopy);
            return true;
        }

        // Mode 3: focused block (caret-only or no cross-block selection)
        int fi = GetFocusedBlockIndex();
        if (fi >= 0 && fi < Blocks.Count)
        {
            var b = Blocks[fi];
            if (b.Type != BlockType.Divider)
            {
                await WriteBlocksToClipboardAsync(new List<BlockViewModel> { b });
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<InlineRun> GetLiveRunsForBlockIndex(int index)
    {
        if (index < 0 || index >= Blocks.Count)
            return Array.Empty<InlineRun>();
        var rte = GetEditableBlockAt(index)?.TryGetRichTextEditor();
        if (rte?.Runs != null)
            return InlineRunFormatApplier.Normalize(new List<InlineRun>(rte.Runs));
        return Blocks[index].Runs;
    }

    /// <summary>Push live <see cref="RichTextEditor.Runs"/> into the view model so clipboard serialization matches the editor.</summary>
    private void SyncViewModelsFromRichEditors(IEnumerable<BlockViewModel> blocks)
    {
        foreach (var b in blocks)
        {
            int idx = Blocks.IndexOf(b);
            if (idx < 0) continue;
            var rte = GetEditableBlockAt(idx)?.TryGetRichTextEditor();
            if (rte == null) continue;
            b.SetRuns(InlineRunFormatApplier.Normalize(new List<InlineRun>(rte.Runs)));
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
            var p = GetBlockMetaString(blocks[0], "imagePath");
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
                    NoteClipboardDiagnostics.Log($"  block[{bi}] type={blocks[bi].Type} {NoteClipboardDiagnostics.SummarizeRuns(blocks[bi].Runs)}");
                await NoteClipboardService.WriteAsync(topLevel.Clipboard, markdown, json, singleImageBitmap).ConfigureAwait(true);
            }
            else if (singleImageBitmap != null)
                await topLevel.Clipboard.SetBitmapAsync(singleImageBitmap).ConfigureAwait(true);
            else
                await topLevel.Clipboard.SetTextAsync(markdown).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            BlockEditorLogger.LogError("Clipboard write failed", ex);
        }
        finally
        {
            singleImageBitmap?.Dispose();
        }
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
                NoteClipboardDiagnostics.Log($"Paste: first block type={pasted[0].Type} {NoteClipboardDiagnostics.SummarizeRuns(pasted[0].Runs)}");

            BeginStructuralChange();

            if (!replaceBlockSelection && pasted.Length >= 1)
            {
                int focusedIndex = GetFocusedBlockIndex();
                if (focusedIndex >= 0 && focusedIndex < Blocks.Count)
                {
                    var focusedVm = Blocks[focusedIndex];
                    if (focusedVm.Type != BlockType.Divider && focusedVm.Type != BlockType.Image
                        && !PasteFirstBlockRequiresBlockInsert(pasted))
                    {
                        var editableBlock = GetEditableBlockAt(focusedIndex);
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
                                focusedVm.Content = textBefore + firstContent;
                                editableBlock!.SetCaretIndex(textBefore.Length + firstContent.Length);

                                if (pasted.Length == 1)
                                {
                                    if (textAfter.Length > 0)
                                    {
                                        var tailBlock = BlockFactory.CreateBlock(BlockType.Text, focusedIndex + 1);
                                        tailBlock.Content = textAfter;
                                        SubscribeToBlock(tailBlock);
                                        Blocks.Insert(focusedIndex + 1, tailBlock);
                                        ReorderBlocks();
                                    }
                                    ClearBlockSelection();
                                    CommitStructuralChange("Paste");
                                    BlocksChanged?.Invoke();
                                    return;
                                }

                                int insertAtCode = focusedIndex + 1;
                                for (int i = 1; i < pasted.Length; i++)
                                {
                                    var block = pasted[i];
                                    string blockContent = (block.Content ?? string.Empty) +
                                        (i == pasted.Length - 1 ? textAfter : string.Empty);
                                    block.Content = blockContent;
                                    SubscribeToBlock(block);
                                    Blocks.Insert(insertAtCode, block);
                                    block.Order = insertAtCode;
                                    insertAtCode++;
                                }
                                ReorderBlocks();
                                ClearBlockSelection();
                                CommitStructuralChange("Paste");
                                BlocksChanged?.Invoke();
                                return;
                            }

                            var liveRunsForPaste = GetLiveRunsForBlockIndex(focusedIndex);
                            var tailRuns = InlineRunFormatApplier.SliceRuns(liveRunsForPaste, end, content.Length);
                            var beforeRuns = InlineRunFormatApplier.SliceRuns(liveRunsForPaste, 0, start);
                            bool promoteStructuralFirst =
                                InlineRunFormatApplier.Flatten(beforeRuns).Length == 0
                                && IsStructuralBlockTypeForLineStartPaste(pasted[0].Type);

                            int caretAfterPaste;
                            if (promoteStructuralFirst)
                            {
                                ApplyPastedStructuralBlockToViewModel(focusedVm, pasted[0]);
                                caretAfterPaste = InlineRunFormatApplier.Flatten(focusedVm.Runs).Length;
                            }
                            else
                            {
                                var pasteFirstRuns = pasted[0].CloneRuns();
                                var mergedFirst = InlineRunFormatApplier.Normalize(
                                    new List<InlineRun>([..beforeRuns, ..pasteFirstRuns]));
                                focusedVm.CommitRunsFromEditor(mergedFirst);
                                caretAfterPaste = start + InlineRunFormatApplier.Flatten(pasteFirstRuns).Length;
                            }

                            editableBlock!.SetCaretIndex(caretAfterPaste);

                            if (pasted.Length == 1)
                            {
                                if (InlineRunFormatApplier.Flatten(tailRuns).Length > 0)
                                {
                                    var tailBlockRich = BlockFactory.CreateBlock(BlockType.Text, focusedIndex + 1);
                                    tailBlockRich.SetRuns(tailRuns);
                                    SubscribeToBlock(tailBlockRich);
                                    Blocks.Insert(focusedIndex + 1, tailBlockRich);
                                    ReorderBlocks();
                                }
                                ClearBlockSelection();
                                CommitStructuralChange("Paste");
                                BlocksChanged?.Invoke();
                                return;
                            }

                            int insertAtRich = focusedIndex + 1;
                            for (int i = 1; i < pasted.Length; i++)
                            {
                                var block = pasted[i];
                                if (i == pasted.Length - 1)
                                {
                                    var mergedLast = InlineRunFormatApplier.Normalize(
                                        new List<InlineRun>([..block.CloneRuns(), ..tailRuns]));
                                    block.SetRuns(mergedLast);
                                }
                                SubscribeToBlock(block);
                                Blocks.Insert(insertAtRich, block);
                                block.Order = insertAtRich;
                                insertAtRich++;
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
                        UnsubscribeFromBlock(block);
                        Blocks.RemoveAt(i);
                    }
                    insertIndex = firstIndex;
                }
            }
            else
            {
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
        catch (Exception ex)
        {
            BlockEditorLogger.LogError("Paste from clipboard failed", ex);
        }
    }

    private static readonly HashSet<string> ClipboardImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif"
    };

    private IImageAssetService? ResolveImageAssetService() =>
        ImageAssetService ?? (Application.Current as App)?.Services?.GetService(typeof(IImageAssetService)) as IImageAssetService;

    private static string GetBlockMetaString(BlockViewModel vm, string key)
    {
        if (!vm.Meta.TryGetValue(key, out var val) || val == null) return string.Empty;
        if (val is string s) return s;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return val.ToString() ?? string.Empty;
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
            var path = GetBlockMetaString(vm, "imagePath");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(fileName, vm.Id, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var r = await svc.ImportAndCopyAsync(path, vm.Id).ConfigureAwait(true);
                if (r.IsSuccess && !string.IsNullOrEmpty(r.Value))
                    vm.Meta["imagePath"] = r.Value!;
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
        vm.Meta["imagePath"] = pathOrExternal;
        vm.Meta["imageAlt"] = string.Empty;
        vm.Meta["imageWidth"] = 0.0;
        vm.SetRuns(new List<InlineRun> { InlineRun.Plain(string.Empty) });
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
        vm.Meta["imagePath"] = path;
        vm.Meta["imageAlt"] = string.Empty;
        vm.Meta["imageWidth"] = 0.0;
        vm.SetRuns(new List<InlineRun> { InlineRun.Plain(string.Empty) });
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

    private int GetFocusedBlockIndex()
    {
        // Use the maintained index; fall back to a linear scan only when stale.
        if (_focusedBlockIndex >= 0 && _focusedBlockIndex < Blocks.Count && Blocks[_focusedBlockIndex].IsFocused)
            return _focusedBlockIndex;

        for (int i = 0; i < Blocks.Count; i++)
        {
            if (Blocks[i].IsFocused)
            {
                _focusedBlockIndex = i;
                return i;
            }
        }
        _focusedBlockIndex = -1;
        return -1;
    }

    #endregion

    /// <summary>
    /// Returns the block index at the given point (in editor coordinates), or -1 if none.
    /// </summary>
    private int GetBlockIndexAtPoint(Point point)
    {
        var bounds = GetBlockBoundsInEditorOrder();
        for (int i = 0; i < bounds.Count; i++)
        {
            var (top, bottom) = bounds[i];
            if (point.Y >= top && point.Y < bottom)
                return i;
        }
        if (bounds.Count > 0 && point.Y >= bounds[^1].Bottom)
            return bounds.Count;
        return -1;
    }

    /// <summary>
    /// Clears text selection (SelectionStart/SelectionEnd) in every block except the one at exceptBlockIndex.
    /// Pass -1 to clear all blocks. Used so a new press or click on empty space clears previous cross-block selection.
    /// </summary>
    private void ClearTextSelectionInAllBlocksExcept(int exceptBlockIndex)
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null) return;
        for (int i = 0; i < Blocks.Count && i < containers.Count; i++)
        {
            if (exceptBlockIndex >= 0 && i == exceptBlockIndex) continue;
            var editableBlock = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
            editableBlock?.ApplyTextSelection(0, 0);
        }
    }

    private void UpdateCrossBlockSelection(Point currentPoint)
    {
        int anchorIndex = _crossBlockAnchorBlockIndex;
        if (anchorIndex < 0 || _crossBlockAnchorBlock == null) return;

        int rawIndex = GetBlockIndexAtPoint(currentPoint);
        if (rawIndex < 0) return;
        rawIndex = Math.Clamp(rawIndex, 0, Blocks.Count - 1);

        // Hysteresis: keep last endpoint block until pointer has clearly left its bounds to avoid boundary flicker
        int currentIndex = rawIndex;
        var bounds = GetBlockBoundsInEditorOrder();
        if (bounds.Count > 0 && _lastCrossBlockCurrentIndex >= 0 && _lastCrossBlockCurrentIndex < bounds.Count)
        {
            var (top, bottom) = bounds[_lastCrossBlockCurrentIndex];
            if (currentPoint.Y >= top && currentPoint.Y < bottom)
                currentIndex = _lastCrossBlockCurrentIndex;
        }
        _lastCrossBlockCurrentIndex = currentIndex;

        int anchorChar = _crossBlockAnchorCharIndex;
        bool forward = currentIndex >= anchorIndex;
        int startIdx = Math.Min(anchorIndex, currentIndex);
        int endIdx = Math.Max(anchorIndex, currentIndex);

        // Text selection only: never use block selection (IsSelected). Clear it on all blocks.
        for (int i = 0; i < Blocks.Count; i++)
            Blocks[i].IsSelected = false;

        var containers = GetBlockContainersInOrder();
        if (containers == null) return;

        for (int i = 0; i < containers.Count && i < Blocks.Count; i++)
        {
            var editableBlock = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
            if (editableBlock == null) continue;

            // Blocks outside the selection range must be explicitly cleared so that shrinking
            // the selection (e.g. dragging back toward the anchor) removes highlights correctly.
            if (i < startIdx || i > endIdx)
            {
                editableBlock.ApplyTextSelection(0, 0);
                continue;
            }

            var block = Blocks[i];
            int len = block.Content?.Length ?? 0;

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

    // Fraction of block height that acts as a "snap-to-boundary" zone.
    // Only the top/bottom portion triggers an insert-before/after decision;
    // the middle portion keeps the current indicator to prevent flicker on
    // multi-line blocks where the midpoint sits inside visible text.
    private const double SnapBandFraction = 0.25;

    /// <summary>
    /// Called by EditableBlock on DragOver. Computes insert index from cursor Y using
    /// snap-band boundaries with hysteresis and updates the drop indicator line.
    /// </summary>
    public void HandleBlockDragOver(Point cursorPosInEditor, BlockViewModel? draggedBlock)
    {
        if (draggedBlock == null || Blocks.Count == 0)
        {
            ClearDropIndicator();
            return;
        }

        var insertIndex = GetInsertIndex(cursorPosInEditor.Y);
        if (insertIndex < 0)
        {
            ClearDropIndicator();
            return;
        }

        // Suppress the indicator for positions that would leave the block where it already is.
        // Inserting at draggedIndex or draggedIndex+1 is a no-op move.
        var draggedIndex = Blocks.IndexOf(draggedBlock);
        if (draggedIndex >= 0 && (insertIndex == draggedIndex || insertIndex == draggedIndex + 1))
        {
            ClearDropIndicator();
            return;
        }

        if (insertIndex == _currentDropInsertIndex && _currentDropIndicatorBlock != null)
            return; // already showing correct line — nothing to do

        ClearDropIndicator();
        _currentDropInsertIndex = insertIndex;

        var blockVisual = GetEditableBlockAt(insertIndex);
        if (blockVisual == null) return;

        _currentDropIndicatorBlock = blockVisual;

        // Show at top of the block we're inserting before; for append-after-last show at bottom.
        if (insertIndex < Blocks.Count)
            blockVisual.ShowDropLineAtTop();
        else
            blockVisual.ShowDropLineAtBottom();
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
        if (_currentDropIndicatorBlock != null)
        {
            _currentDropIndicatorBlock.HideDropLine();
            _currentDropIndicatorBlock = null;
        }
        _currentDropInsertIndex = -1;
    }

    /// <summary>
    /// Perform the drop: move draggedBlock to CurrentDropInsertIndex.
    /// </summary>
    public bool TryPerformDrop(BlockViewModel draggedBlock)
    {
        if (_currentDropInsertIndex < 0 || _currentDropInsertIndex > Blocks.Count)
            return false;
        var draggedIndex = Blocks.IndexOf(draggedBlock);
        if (draggedIndex < 0) return false;

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

    private int GetInsertIndex(double cursorY)
    {
        var blockBounds = GetBlockBoundsInEditorOrder();
        if (blockBounds.Count == 0)
            return -1;

        // Each block is divided into three vertical zones:
        //   top snap-band    → insert before this block   (insertIndex = i)
        //   bottom snap-band → insert after this block    (insertIndex = i + 1)
        //   middle dead zone → keep the current index (hysteresis — prevents the indicator
        //                      from jumping while moving through a tall/multi-line block)
        //
        // The indicator is ALWAYS shown at the TOP of the block at insertIndex
        // (or the bottom of the last block when insertIndex == Count).
        // This guarantees one insert index → one visual location with no ambiguity.

        for (int i = 0; i < blockBounds.Count; i++)
        {
            var (top, bottom) = blockBounds[i];
            if (cursorY < top || cursorY >= bottom) continue; // cursor not in this block

            var height = bottom - top;
            var snapBand = Math.Max(4, height * SnapBandFraction);

            if (cursorY < top + snapBand)
                return i; // top snap-band → insert before block i

            if (cursorY >= bottom - snapBand)
                return i + 1; // bottom snap-band → insert after block i

            // Middle dead zone: preserve the current index if it is adjacent to this block.
            if (_currentDropInsertIndex == i || _currentDropInsertIndex == i + 1)
                return _currentDropInsertIndex;

            // No prior index or it jumped far — fall back to the closer boundary.
            var midY = (top + bottom) / 2.0;
            return cursorY < midY ? i : i + 1;
        }

        // Cursor is below all blocks → append at end.
        return blockBounds.Count;
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

    private EditableBlock? GetEditableBlockAt(int insertIndex)
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null || containers.Count == 0) return null;

        // insertIndex == Count means "append after last" — attach indicator to the last block.
        var index = Math.Min(insertIndex, containers.Count - 1);
        if (index < 0) return null;

        var container = containers[index];
        return container as EditableBlock ?? container.GetVisualDescendants().OfType<EditableBlock>().FirstOrDefault();
    }

    #endregion

    #region History helpers

    private static string Truncate(string? s, int max = 40) =>
        s == null ? "<null>" : s.Length <= max ? $"'{s}'" : $"'{s[..max]}…'";

    private BlockSnapshot[] CaptureSnapshot()
    {
        var snapshots = new BlockSnapshot[Blocks.Count];
        for (int i = 0; i < Blocks.Count; i++)
        {
            var b = Blocks[i];
            snapshots[i] = new BlockSnapshot(
                b.Id, b.Type, b.CloneRuns(),
                b.Meta ?? new Dictionary<string, object>(), i);
        }
        return snapshots;
    }

    private CaretState? CaptureCaretState()
    {
        int idx = GetFocusedBlockIndex();
        if (idx < 0) return null;
        var editableBlock = GetEditableBlockAt(idx);
        var caretIdx = editableBlock?.GetCaretIndex();
        return new CaretState { BlockIndex = idx, CaretPosition = caretIdx ?? 0 };
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
        if (_pendingSnapshot != null)
            BlockEditorLogger.Log("BeginStructuralChange: overwriting orphaned _pendingSnapshot");
        _pendingSnapshot = CaptureSnapshot();
        _pendingCaretBefore = CaptureCaretState();
        BlockEditorLogger.Log($"BeginStructuralChange: captured {_pendingSnapshot.Length} blocks");
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

        BlockEditorLogger.Log($"CommitStructuralChange: PUSH DocumentOp '{description}' before={before.Length}blocks after={after.Length}blocks");

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
            var existingById = new Dictionary<string, BlockViewModel>();
            foreach (var b in Blocks)
                existingById[b.Id] = b;

            var newList = new List<BlockViewModel>(targetBlocks.Length);
            var usedIds = new HashSet<string>();

            foreach (var blk in targetBlocks.OrderBy(b => b.Order))
            {
                if (existingById.TryGetValue(blk.Id, out var existing) && usedIds.Add(blk.Id))
                {
                    blk.EnsureInlineRuns();
                    existing.SetRuns(blk.InlineRuns!);
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

            // Unsubscribe from blocks that are no longer present
            foreach (var kvp in existingById)
            {
                if (!usedIds.Contains(kvp.Key))
                    UnsubscribeFromBlock(kvp.Value);
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
    private void RestoreBlockRuns(string blockId, List<InlineRun> runs, CaretState? caret)
    {
        _isRestoringFromHistory = true;
        try
        {
            var vm = Blocks.FirstOrDefault(b => b.Id == blockId);
            if (vm != null)
            {
                vm.SetRuns(runs);
                BlockEditorLogger.Log($"RestoreBlockRuns blockId={blockId} text='{Truncate(vm.Content)}'");
            }
            else
            {
                BlockEditorLogger.Log($"RestoreBlockRuns blockId={blockId} NOT FOUND in Blocks");
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
        if (caret == null || caret.BlockIndex < 0 || caret.BlockIndex >= Blocks.Count)
            return;

        // Clear stale VM focus flags first; rapid undo can leave IsFocused=true while
        // the real TextBox has already lost focus.
        for (int i = 0; i < Blocks.Count; i++)
            Blocks[i].IsFocused = false;

        var blockIndex = caret.BlockIndex;
        var caretPos = caret.CaretPosition;
        var target = Blocks[blockIndex];
        target.PendingCaretIndex = caretPos;

        // Force a fresh focus transition at Input priority for rapid undo/redo stability.
        Dispatcher.UIThread.Post(() =>
        {
            if (blockIndex < 0 || blockIndex >= Blocks.Count) return;
            var latestTarget = Blocks[blockIndex];
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
    internal void TrackTypingEdit(BlockViewModel block, string previousText, List<InlineRun>? previousRuns = null)
    {
        if (_history == null || _isRestoringFromHistory)
        {
            BlockEditorLogger.Log($"TrackTypingEdit skipped: history={_history != null} restoring={_isRestoringFromHistory}");
            return;
        }

        var idx = Blocks.IndexOf(block);

        if (_typingBatchBlockId != null && _typingBatchBlockId != block.Id)
        {
            BlockEditorLogger.Log($"TrackTypingEdit: block switch {_typingBatchBlockId} -> {block.Id}, flushing");
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
                _typingBatchRunsBefore = block.CloneRuns();
                // Reconstruct the runs as they were *before* this edit using the previous text
                if (previousText != block.Content)
                {
                    _typingBatchRunsBefore = Core.Formatting.InlineRunFormatApplier.ApplyTextEdit(
                        _typingBatchRunsBefore, block.Content, previousText);
                }
            }
            _typingBatchCaretBefore = CaptureCaretState() ?? new CaretState { BlockIndex = idx, CaretPosition = 0 };
            BlockEditorLogger.Log($"TrackTypingEdit: NEW batch for block {block.Id} before={Truncate(previousText)} current={Truncate(block.Content)}");
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

        var vm = Blocks.FirstOrDefault(b => b.Id == _typingBatchBlockId);
        if (vm == null)
        {
            BlockEditorLogger.Log($"FlushTypingBatch: block {_typingBatchBlockId} not found, discarding");
            _typingBatchBlockId = null;
            _typingBatchRunsBefore = null;
            _typingBatchCaretBefore = null;
            return;
        }

        var runsAfter = vm.CloneRuns();
        var runsBefore = _typingBatchRunsBefore ?? new List<InlineRun> { InlineRun.Plain(string.Empty) };

        bool runsEqual = runsBefore.Count == runsAfter.Count && runsBefore.SequenceEqual(runsAfter);

        if (runsEqual)
        {
            BlockEditorLogger.Log($"FlushTypingBatch: no-op (runs equal) block={_typingBatchBlockId}");
            _typingBatchBlockId = null;
            _typingBatchRunsBefore = null;
            _typingBatchCaretBefore = null;
            return;
        }

        var textBefore = Core.Formatting.InlineRunFormatApplier.Flatten(runsBefore);
        var textAfter = vm.Content ?? string.Empty;

        var idx = Blocks.IndexOf(vm);
        var editableBlock = GetEditableBlockAt(idx);
        var caretIdx = editableBlock?.GetCaretIndex();
        var caretAfter = new CaretState { BlockIndex = idx, CaretPosition = caretIdx ?? 0 };

        BlockEditorLogger.Log($"FlushTypingBatch: PUSH TextEditOp block={vm.Id} before={Truncate(textBefore)} after={Truncate(textAfter)}");

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
            BlockEditorLogger.Log($"UndoAsync: nothing to undo (history={_history != null} canUndo={_history?.CanUndo})");
            return;
        }
        FlushTypingBatch();
        BlockEditorLogger.Log("UndoAsync: executing");
        await _history.UndoAsync();
    }

    public async Task RedoAsync()
    {
        if (_history == null || !_history.CanRedo)
        {
            BlockEditorLogger.Log($"RedoAsync: nothing to redo (history={_history != null} canRedo={_history?.CanRedo})");
            return;
        }
        BlockEditorLogger.Log("RedoAsync: executing");
        await _history.RedoAsync();
    }

    #endregion

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


