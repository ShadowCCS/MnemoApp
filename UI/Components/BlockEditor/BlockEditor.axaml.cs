using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MnemoApp.Modules.Notes.Models;

namespace MnemoApp.UI.Components.BlockEditor;

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

        Blocks.Clear();

        if (blocks == null || blocks.Length == 0)
        {
            AddBlock(BlockType.Text);
            return;
        }

        // Use a HashSet to track block IDs and prevent duplicates
        var seenIds = new HashSet<string>();
        foreach (var block in blocks.OrderBy(b => b.Order))
        {
            // Skip duplicate blocks with the same ID
            if (!string.IsNullOrEmpty(block.Id) && seenIds.Contains(block.Id))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(block.Id))
            {
                seenIds.Add(block.Id);
            }

            var viewModel = new BlockViewModel(block);
            SubscribeToBlock(viewModel);
            Blocks.Add(viewModel);
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
            ReorderBlocks();
        }
        else
        {
            Blocks.Add(block);
        }
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
        // Trigger save in parent
        BlocksChanged?.Invoke();
    }

    private void OnBlockDeleteRequested(BlockViewModel block)
    {
        if (Blocks.Count == 1)
        {
            // Don't delete the last block, just clear it
            block.Content = string.Empty;
            block.Type = BlockType.Text;
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index == -1) return; // Block not found, safety check
        
        UnsubscribeFromBlock(block);
        Blocks.Remove(block);

        // Focus previous block if exists
        if (index > 0 && Blocks.Count > 0)
        {
            var targetIndex = Math.Min(index - 1, Blocks.Count - 1);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Blocks[targetIndex].IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Input);
        }
        else if (Blocks.Count > 0)
        {
            // If we deleted the first block, focus the new first block
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Blocks[0].IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Input);
        }

        ReorderBlocks();
        BlocksChanged?.Invoke();
    }

    private void OnNewBlockRequested(BlockViewModel block)
    {
        var index = Blocks.IndexOf(block);
        AddBlock(BlockType.Text, index + 1);

        // Focus new block after UI has updated
        if (index + 1 < Blocks.Count)
        {
            var newBlock = Blocks[index + 1];
            // Use dispatcher to ensure the new block is rendered before focusing
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                newBlock.IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Render);
        }

        BlocksChanged?.Invoke();
    }

    private void OnNewBlockOfTypeRequested(BlockViewModel block, BlockType type)
    {
        var index = Blocks.IndexOf(block);
        AddBlock(type, index + 1);

        // Focus new block after UI has updated
        if (index + 1 < Blocks.Count)
        {
            var newBlock = Blocks[index + 1];
            // Use dispatcher to ensure the new block is rendered before focusing
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                newBlock.IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Render);
        }

        BlocksChanged?.Invoke();
    }

    private void OnDeleteAndFocusAboveRequested(BlockViewModel block)
    {
        if (Blocks.Count == 1)
        {
            // Don't delete the last block, just clear it
            block.Content = string.Empty;
            block.Type = BlockType.Text;
            return;
        }

        var index = Blocks.IndexOf(block);
        if (index == -1) return; // Block not found, safety check
        
        UnsubscribeFromBlock(block);
        Blocks.Remove(block);

        // Focus previous block if exists (should always exist at this point)
        if (index > 0 && Blocks.Count > 0)
        {
            var targetIndex = Math.Min(index - 1, Blocks.Count - 1);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Blocks[targetIndex].IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Input);
        }
        else if (Blocks.Count > 0)
        {
            // If we deleted the first block, focus the new first block
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Blocks[0].IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Input);
        }

        ReorderBlocks();
        BlocksChanged?.Invoke();
    }

    private void OnFocusPreviousRequested(BlockViewModel block)
    {
        var index = Blocks.IndexOf(block);
        if (index > 0)
        {
            // Clear focus from current block first
            block.IsFocused = false;
            
            // Focus previous block with a slight delay to ensure UI updates
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Blocks[index - 1].IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    private void OnFocusNextRequested(BlockViewModel block)
    {
        var index = Blocks.IndexOf(block);
        if (index < Blocks.Count - 1)
        {
            // Clear focus from current block first
            block.IsFocused = false;
            
            // Focus next block with a slight delay to ensure UI updates
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Blocks[index + 1].IsFocused = true;
            }, Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    private void ReorderBlocks()
    {
        for (int i = 0; i < Blocks.Count; i++)
        {
            Blocks[i].Order = i;
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

