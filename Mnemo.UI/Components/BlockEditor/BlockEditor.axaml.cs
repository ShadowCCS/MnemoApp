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

    public ObservableCollection<BlockViewModel> Blocks
    {
        get => _blocks;
        set
        {
            _blocks = value;
            OnPropertyChanged();
        }
    }

    public BlockEditor()
    {
        InitializeComponent();
        DataContext = this;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragLeaveEvent, Editor_DragLeave, RoutingStrategies.Bubble);

        // Don't add initial block here - let LoadBlocks handle it
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
        block.DeleteRequested -= OnBlockDeleteRequested;
        block.NewBlockRequested -= OnNewBlockRequested;
        block.NewBlockOfTypeRequested -= OnNewBlockOfTypeRequested;
        block.DeleteAndFocusAboveRequested -= OnDeleteAndFocusAboveRequested;
        block.FocusPreviousRequested -= OnFocusPreviousRequested;
        block.FocusNextRequested -= OnFocusNextRequested;
        block.MergeWithPreviousRequested -= OnMergeWithPreviousRequested;
    }

    private void OnBlockContentChanged(BlockViewModel block)
    {
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


