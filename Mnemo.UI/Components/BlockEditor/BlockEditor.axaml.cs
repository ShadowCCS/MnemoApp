using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

public partial class BlockEditor : UserControl, INotifyPropertyChanged
{
    private ObservableCollection<BlockViewModel> _blocks = new();

    // Drag-box block selection (Mode 2)
    private bool _isBoxSelecting;
    private bool _boxSelectArmed;   // true after pointer-down outside blocks, waiting for threshold
    private Point _boxSelectStart;
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
    private int _crossBlockAnchorCharIndex;
    private Point _crossBlockStartPoint;
    /// <summary>Hysteresis: last endpoint block index to avoid boundary flicker when dragging across blocks.</summary>
    private int _lastCrossBlockCurrentIndex = -1;

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
        AddHandler(PointerPressedEvent, Editor_PointerPressedBubble, RoutingStrategies.Bubble);
        // When we capture the pointer (cross-block or box-select), we receive moves/releases on this control
        AddHandler(PointerMovedEvent, Editor_PointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Editor_PointerReleased, RoutingStrategies.Tunnel);
        // Tunnel so we get keys before the focused TextBox (for block-selection Backspace/Copy/Paste)
        AddHandler(KeyDownEvent, Editor_KeyDown, RoutingStrategies.Tunnel);
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
        block.NewBlockRequested += OnNewBlockRequested;
        block.NewBlockOfTypeRequested += OnNewBlockOfTypeRequested;
        block.DeleteAndFocusAboveRequested += OnDeleteAndFocusAboveRequested;
        block.FocusPreviousRequested += OnFocusPreviousRequested;
        block.FocusNextRequested += OnFocusNextRequested;
        block.MergeWithPreviousRequested += OnMergeWithPreviousRequested;
    }

    private void UnsubscribeFromBlock(BlockViewModel block)
    {
        block.ContentChanged -= OnBlockContentChanged;
        block.PropertyChanged -= OnBlockPropertyChanged;
        block.DeleteRequested -= OnBlockDeleteRequested;
        block.NewBlockRequested -= OnNewBlockRequested;
        block.NewBlockOfTypeRequested -= OnNewBlockOfTypeRequested;
        block.DeleteAndFocusAboveRequested -= OnDeleteAndFocusAboveRequested;
        block.FocusPreviousRequested -= OnFocusPreviousRequested;
        block.FocusNextRequested -= OnFocusNextRequested;
        block.MergeWithPreviousRequested -= OnMergeWithPreviousRequested;
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BlockViewModel.IsFocused)) return;
        if (sender is BlockViewModel block && block.IsFocused)
        {
            // During cross-block text selection the editor controls which blocks have text
            // selection. Clearing it here (triggered by IsFocused changes on each block we
            // update) would fight with UpdateCrossBlockSelection and cause flicker.
            if (_isCrossBlockSelecting || _crossBlockArmed) return;

            ClearBlockSelection();
            var idx = Blocks.IndexOf(block);
            if (idx >= 0)
                ClearTextSelectionInAllBlocksExcept(idx);
        }
    }

    private void OnBlockContentChanged(BlockViewModel block)
    {
        // Clear block selection when user edits text (typing, paste, etc.)
        ClearBlockSelection();
        // Update list numbers in case block type changed
        UpdateListNumbers();
        // Trigger save in parent
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
        if (index <= 0) return; // No previous block to merge into

        var previousBlock = Blocks[index - 1];
        var insertionPoint = previousBlock.Content?.Length ?? 0;
        previousBlock.Content = (previousBlock.Content ?? string.Empty) + (block.Content ?? string.Empty);

        UnsubscribeFromBlock(block);
        Blocks.Remove(block);

        ReorderBlocks();
        BlocksChanged?.Invoke();

        // Set PendingCaretIndex before IsFocused so the IsFocused handler can read it
        var caretTarget = insertionPoint;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                previousBlock.PendingCaretIndex = caretTarget;
                previousBlock.IsFocused = true;
            },
            Avalonia.Threading.DispatcherPriority.Input);
    }

    private void DeleteBlock(BlockViewModel block)
    {
        var index = Blocks.IndexOf(block);
        if (index == -1) return; // Block not found, safety check

        // Don't delete if it's the only block - just clear and keep as text
        if (Blocks.Count == 1)
        {
            block.Content = string.Empty;
            block.Type = BlockType.Text;
            block.IsFocused = true; // Keep focus on the cleared block
            UpdateListNumbers(); // Update list numbers in case type changed
            return;
        }

        UnsubscribeFromBlock(block);
        Blocks.Remove(block);

        // Focus previous block (index > 0) or new first block (index == 0)
        var targetIndex = index > 0 ? index - 1 : 0;
        if (Blocks.Count > targetIndex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => Blocks[targetIndex].IsFocused = true, 
                Avalonia.Threading.DispatcherPriority.Input);
        }

        ReorderBlocks();
        BlocksChanged?.Invoke();
    }

    private void OnNewBlockRequested(BlockViewModel block, string? initialContent)
    {
        var index = Blocks.IndexOf(block);
        AddBlock(BlockType.Text, index + 1, initialContent);

        // Focus new block after UI has updated
        var newBlockIndex = index + 1;
        if (newBlockIndex < Blocks.Count)
        {
            var newBlock = Blocks[newBlockIndex];
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => newBlock.IsFocused = true, 
                Avalonia.Threading.DispatcherPriority.Render);
        }

        BlocksChanged?.Invoke();
    }

    private void OnNewBlockOfTypeRequested(BlockViewModel block, BlockType type)
    {
        var index = Blocks.IndexOf(block);
        AddBlock(type, index + 1);

        // Focus new block after UI has updated
        var newBlockIndex = index + 1;
        if (newBlockIndex < Blocks.Count)
        {
            var newBlock = Blocks[newBlockIndex];
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => newBlock.IsFocused = true, 
                Avalonia.Threading.DispatcherPriority.Render);
        }

        BlocksChanged?.Invoke();
    }

    private void OnFocusPreviousRequested(BlockViewModel block)
    {
        var index = Blocks.IndexOf(block);
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
        var index = Blocks.IndexOf(block);
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
        for (int i = 0; i < Blocks.Count; i++)
        {
            Blocks[i].Order = i;
        }
        
        // Update list numbers for numbered list blocks
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
    /// Tunnel: when press is inside a block, capture immediately for cross-block text selection so we receive
    /// PointerMoved (TextBox would otherwise capture and we would never see moves). When press is on empty
    /// space, arm box-select and capture in PointerMoved once threshold is exceeded.
    /// </summary>
    private void Editor_PointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Double/triple clicks must reach the TextBox for word/line selection — don't intercept them.
        if (e.ClickCount > 1) return;

        // If the press originated on the drag handle, let it propagate so DoDragDrop can run.
        var source = e.Source as Visual;
        bool hitIsDragHandle = source != null &&
            (source is Border { Tag: "DragHandle" } ||
             source.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is "DragHandle"));
        if (hitIsDragHandle) return;

        var pos = e.GetPosition(this);

        if (IsPointInsideAnyBlock(pos))
        {
            // Press inside a block: arm cross-block selection and capture so we get PointerMoved
            var blockIndex = GetBlockIndexAtPoint(pos);
            if (blockIndex < 0 || blockIndex >= Blocks.Count) return;

            var editableBlock = GetEditableBlockAt(blockIndex);
            if (editableBlock == null) return;

            var pointInBlock = this.TranslatePoint(pos, editableBlock);
            if (!pointInBlock.HasValue) return;

            var vm = Blocks[blockIndex];
            var charIndex = editableBlock.GetCharacterIndexFromPoint(pointInBlock.Value);

            ClearBlockSelection();
            _crossBlockAnchorBlock = vm;
            _crossBlockAnchorCharIndex = Math.Clamp(charIndex, 0, vm.Content?.Length ?? 0);
            _crossBlockArmed = true;
            _isCrossBlockSelecting = false;

            editableBlock.ApplyTextSelection(_crossBlockAnchorCharIndex, _crossBlockAnchorCharIndex);
            ClearTextSelectionInAllBlocksExcept(blockIndex);
            // Set PendingCaretIndex before IsFocused so FocusTextBox lands at the click position
            // directly — without this it would snap to the end first, causing a visible flicker.
            vm.PendingCaretIndex = _crossBlockAnchorCharIndex;
            vm.IsFocused = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Arm box-select — actual capture happens in PointerMoved once threshold is exceeded
        _boxSelectStart = pos;
        _boxSelectArmed = true;
        _isBoxSelecting = false;

        ClearBlockSelection();
        ClearTextSelectionInAllBlocksExcept(-1);
    }

    /// <summary>
    /// Returns true if the point (in editor coordinates) falls within the rendered bounds of any block container.
    /// </summary>
    private bool IsPointInsideAnyBlock(Point point)
    {
        var containers = GetBlockContainersInOrder();
        if (containers == null) return false;
        foreach (var container in containers)
        {
            var topLeft = container.TranslatePoint(new Point(0, 0), this);
            if (topLeft == null) continue;
            var bounds = new Rect(topLeft.Value.X, topLeft.Value.Y, container.Bounds.Width, container.Bounds.Height);
            if (bounds.Contains(point)) return true;
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
        bool hitIsInTextBox = source is TextBox || (source != null && source.GetVisualAncestors().Any(a => a is TextBox));
        if (!hitIsInTextBox || source == null) return;

        var textBox = source as TextBox ?? source.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        var editableBlock = source as EditableBlock ?? source.GetVisualAncestors().OfType<EditableBlock>().FirstOrDefault();
        if (textBox == null || editableBlock == null || editableBlock.DataContext is not BlockViewModel vm || !Blocks.Contains(vm))
            return;

        // Arm cross-block select — capture happens in PointerMoved once drag leaves the source block
        _crossBlockAnchorBlock = vm;
        _crossBlockAnchorCharIndex = Math.Clamp(textBox.CaretIndex, 0, textBox.Text?.Length ?? 0);
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
                    _selectionBoxBorder.Margin = new Thickness(_boxSelectStart.X - EditorContentPaddingX, _boxSelectStart.Y, 0, 0);
                    _selectionBoxBorder.Width = 0;
                    _selectionBoxBorder.Height = 0;
                }
            }
        }

        if (_isBoxSelecting)
        {
            UpdateSelectionBoxVisual(_boxSelectStart, current);
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
        var wasCrossBlockSelecting = _isCrossBlockSelecting;
        var wasArmedButNotDragged = _crossBlockArmed && !_isCrossBlockSelecting && _crossBlockAnchorBlock != null;
        var clickAnchorBlock = _crossBlockAnchorBlock;
        var clickAnchorChar = _crossBlockAnchorCharIndex;

        _boxSelectArmed = false;
        _crossBlockArmed = false;
        _isBoxSelecting = false;
        _isCrossBlockSelecting = false;
        _crossBlockAnchorBlock = null;
        _lastCrossBlockCurrentIndex = -1;

        if (wasBoxSelecting)
        {
            e.Pointer.Capture(null);
            if (_selectionBoxBorder != null)
                _selectionBoxBorder.IsVisible = false;
        }
        else if (wasCrossBlockSelecting)
        {
            e.Pointer.Capture(null);
        }
        else if (wasArmedButNotDragged && clickAnchorBlock != null)
        {
            // Plain click: focus+caret were already set at press time via PendingCaretIndex.
            // Just release the pointer capture that the tunnel handler acquired.
            e.Pointer.Capture(null);
        }
    }

    private void UpdateSelectionBoxVisual(Point start, Point end)
    {
        if (_selectionBoxBorder == null) return;
        double x = Math.Min(start.X, end.X) - EditorContentPaddingX;
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        _selectionBoxBorder.Margin = new Thickness(x, y, 0, 0);
        _selectionBoxBorder.Width = width;
        _selectionBoxBorder.Height = height;
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
            var container = containers[i];
            var topLeft = container.TranslatePoint(new Point(0, 0), this);
            if (topLeft == null) continue;
            var bounds = new Rect(topLeft.Value.X, topLeft.Value.Y, container.Bounds.Width, container.Bounds.Height);
            Blocks[i].IsSelected = selectionRect.Intersects(bounds);
        }
    }

    #region Clipboard and block-selection keyboard (copy as markdown, paste as blocks, backspace deletes selection)

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        var hasBlockSelection = Blocks.Any(b => b.IsSelected);

        // 1. Backspace: delete all selected blocks
        if (e.Key == Key.Back && hasBlockSelection)
        {
            DeleteSelectedBlocks();
            e.Handled = true;
            return;
        }

        // 2. Ctrl+C: copy selected blocks (or cross-block text selection) as markdown
        bool ctrlC = e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlC)
        {
            if (TryCopySelectionToClipboard())
                e.Handled = true;
            return;
        }

        // 3. Ctrl+V: paste markdown as blocks (replacing block selection when present)
        bool ctrlV = e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (ctrlV)
        {
            if (TryPasteFromClipboard(hasBlockSelection))
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

        int firstIndex = selectedIndices.Min();
        // Remove from highest index downward so indices stay valid
        var toRemove = selectedIndices.OrderByDescending(x => x).ToList();
        foreach (int i in toRemove)
        {
            var block = Blocks[i];
            UnsubscribeFromBlock(block);
            Blocks.RemoveAt(i);
        }

        // Ensure at least one block remains
        if (Blocks.Count == 0)
        {
            var defaultBlock = BlockFactory.CreateBlock(BlockType.Text, 0);
            SubscribeToBlock(defaultBlock);
            Blocks.Add(defaultBlock);
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => defaultBlock.IsFocused = true,
                Avalonia.Threading.DispatcherPriority.Input);
        }
        else
        {
            // Focus the block that replaced the first deleted one
            int focusIndex = Math.Min(firstIndex, Blocks.Count - 1);
            if (focusIndex < 0) focusIndex = 0;
            var target = Blocks[focusIndex];
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => target.IsFocused = true,
                Avalonia.Threading.DispatcherPriority.Input);
        }

        ReorderBlocks();
        ClearBlockSelection();
        BlocksChanged?.Invoke();
    }

    private bool TryCopySelectionToClipboard()
    {
        // Mode 2: block selection (drag-box)
        var selectedBlocks = Blocks.Where(b => b.IsSelected).ToList();
        if (selectedBlocks.Count > 0)
        {
            var markdown = BlockMarkdownSerializer.Serialize(selectedBlocks);
            return SetClipboardText(markdown, this);
        }

        // Mode 1: cross-block text selection (gather blocks that have text selection)
        var containers = GetBlockContainersInOrder();
        if (containers == null) return false;

        var toCopy = new List<BlockViewModel>();
        for (int i = 0; i < Blocks.Count && i < containers.Count; i++)
        {
            var editableBlock = GetEditableBlockAt(i) ?? (containers[i] as EditableBlock);
            if (editableBlock == null) continue;
            var selectedText = editableBlock.GetSelectedText();
            if (string.IsNullOrEmpty(selectedText)) continue;
            var block = Blocks[i];
            var vm = BlockFactory.CreateBlock(block.Type, toCopy.Count);
            vm.Content = selectedText;
            if (block.Type == BlockType.Checklist)
                vm.IsChecked = block.IsChecked;
            toCopy.Add(vm);
        }
        if (toCopy.Count == 0) return false;
        var md = BlockMarkdownSerializer.Serialize(toCopy);
        return SetClipboardText(md, this);
    }

    private static bool SetClipboardText(string text, Visual? relativeTo)
    {
        try
        {
            var topLevel = relativeTo != null ? TopLevel.GetTopLevel(relativeTo) : null;
            if (topLevel?.Clipboard != null)
            {
                topLevel.Clipboard.SetTextAsync(text).GetAwaiter().GetResult();
                return true;
            }
        }
        catch { /* clipboard can fail */ }
        return false;
    }

    private bool TryPasteFromClipboard(bool replaceBlockSelection)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return false;

            var text = topLevel.Clipboard.GetTextAsync().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var pasted = BlockMarkdownSerializer.Deserialize(text);
            if (pasted.Length == 0) return false;

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
            BlocksChanged?.Invoke();

            if (pasted.Length > 0)
            {
                var firstPasted = pasted[0];
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => firstPasted.IsFocused = true,
                    Avalonia.Threading.DispatcherPriority.Input);
            }
            return true;
        }
        catch { return false; }
    }

    private int GetFocusedBlockIndex()
    {
        for (int i = 0; i < Blocks.Count; i++)
        {
            if (Blocks[i].IsFocused) return i;
        }
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
        int anchorIndex = _crossBlockAnchorBlock != null ? Blocks.IndexOf(_crossBlockAnchorBlock) : -1;
        if (anchorIndex < 0) return;

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

        // _currentDropInsertIndex is an insert-before index (0..Count).
        // ObservableCollection.Move(old, new) places the item at the *final* position after removal.
        // When draggedIndex < insertIndex the removal shifts all subsequent items left by 1,
        // so the final index must be decremented by 1 to land in the right slot.
        var insertIndex = Math.Min(_currentDropInsertIndex, Blocks.Count - 1);
        var targetIndex = draggedIndex < insertIndex ? insertIndex - 1 : insertIndex;

        if (draggedIndex == targetIndex) return false;

        Blocks.Move(draggedIndex, targetIndex);
        for (int i = 0; i < Blocks.Count; i++)
            Blocks[i].Order = i;
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

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


