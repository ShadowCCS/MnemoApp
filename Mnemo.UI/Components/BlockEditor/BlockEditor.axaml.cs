using Avalonia.Controls;
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

        // Don't add initial block here - let LoadBlocks handle it
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

    public void AddBlock(BlockType type, int? position = null)
    {
        var order = position ?? Blocks.Count;
        var block = BlockFactory.CreateBlock(type, order);
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

    private void DeleteBlock(BlockViewModel block)
    {
        // Never delete if it's the only block - just clear it
        if (Blocks.Count <= 1)
        {
            block.Content = string.Empty;
            block.Type = BlockType.Text;
            block.IsFocused = true; // Keep focus on the cleared block
            UpdateListNumbers(); // Update list numbers in case type changed
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index == -1) return; // Block not found, safety check
        
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

    private void OnNewBlockRequested(BlockViewModel block)
    {
        var index = Blocks.IndexOf(block);
        AddBlock(BlockType.Text, index + 1);

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

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


