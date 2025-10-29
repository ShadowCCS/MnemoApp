using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MnemoApp.Modules.Notes.Models;

namespace MnemoApp.UI.Components.BlockEditor;

public class BlockViewModel : INotifyPropertyChanged
{
    private string _id;
    private BlockType _type;
    private string _content;
    private Dictionary<string, object> _meta;
    private int _order;
    private bool _isFocused;
    private bool _isSelected;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public BlockType Type
    {
        get => _type;
        set 
        { 
            _type = value; 
            EnsureMetaKeys();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Watermark)); // Update watermark when type changes
        }
    }

    public string Content
    {
        get => _content;
        set 
        { 
            _content = value; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(Watermark)); // Update watermark when content changes
        }
    }

    public Dictionary<string, object> Meta
    {
        get => _meta;
        set 
        { 
            _meta = value ?? new Dictionary<string, object>(); 
            EnsureMetaKeys();
            OnPropertyChanged(); 
        }
    }

    public int Order
    {
        get => _order;
        set 
        { 
            _order = value; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(ListNumber)); // Update list number when order changes
        }
    }

    public string ListNumber => $"{Order + 1}.";

    public bool IsFocused
    {
        get => _isFocused;
        set 
        { 
            _isFocused = value; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(Watermark)); // Update watermark when focus changes
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsChecked
    {
        get => _meta.TryGetValue("checked", out var value) && value is bool boolValue && boolValue;
        set 
        { 
            _meta["checked"] = value;
            OnPropertyChanged();
        }
    }

    public string Watermark
    {
        get
        {
            // Only show the slash command hint for empty, focused Text blocks
            if (Type == BlockType.Text && IsFocused && string.IsNullOrEmpty(Content))
            {
                return "Type '/' for commands...";
            }
            
            return Type switch
            {
                BlockType.Text => string.Empty,
                BlockType.Heading1 => "Heading 1",
                BlockType.Heading2 => "Heading 2",
                BlockType.Heading3 => "Heading 3",
                BlockType.Quote => "Quote",
                BlockType.Code => "Code",
                BlockType.BulletList => "List item",
                BlockType.NumberedList => "List item",
                BlockType.Checklist => "To-do",
                _ => string.Empty
            };
        }
    }

    public event Action<BlockViewModel>? ContentChanged;
    public event Action<BlockViewModel>? DeleteRequested;
    public event Action<BlockViewModel>? NewBlockRequested;
    public event Action<BlockViewModel, BlockType>? NewBlockOfTypeRequested;
    public event Action<BlockViewModel>? DeleteAndFocusAboveRequested;
    public event Action<BlockViewModel>? FocusPreviousRequested;
    public event Action<BlockViewModel>? FocusNextRequested;

    public BlockViewModel(BlockType type, string content = "", int order = 0)
    {
        _id = Guid.NewGuid().ToString();
        _type = type;
        _content = content;
        _meta = new Dictionary<string, object>();
        _order = order;
        
        // Ensure required meta keys exist based on block type
        EnsureMetaKeys();
    }

    public BlockViewModel(Block block)
    {
        _id = block.Id;
        _type = block.Type;
        _content = block.Content;
        _meta = new Dictionary<string, object>(block.Meta ?? new Dictionary<string, object>());
        _order = block.Order;
        
        // Ensure required meta keys exist based on block type
        EnsureMetaKeys();
    }
    
    private void EnsureMetaKeys()
    {
        bool metaChanged = false;
        
        switch (_type)
        {
            case BlockType.Checklist:
                if (!_meta.ContainsKey("checked"))
                {
                    _meta["checked"] = false;
                    metaChanged = true;
                }
                break;
            case BlockType.Code:
                if (!_meta.ContainsKey("language"))
                {
                    _meta["language"] = "csharp";
                    metaChanged = true;
                }
                break;
        }
        
        if (metaChanged)
        {
            OnPropertyChanged(nameof(Meta));
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    public Block ToBlock()
    {
        return new Block
        {
            Id = Id,
            Type = Type,
            Content = Content,
            Meta = Meta,
            Order = Order
        };
    }

    public void NotifyContentChanged()
    {
        ContentChanged?.Invoke(this);
    }

    public void RequestDelete()
    {
        DeleteRequested?.Invoke(this);
    }

    public void RequestNewBlock()
    {
        NewBlockRequested?.Invoke(this);
    }

    public void RequestNewBlockOfType(BlockType type)
    {
        NewBlockOfTypeRequested?.Invoke(this, type);
    }

    public void RequestDeleteAndFocusAbove()
    {
        DeleteAndFocusAboveRequested?.Invoke(this);
    }

    public void RequestFocusPrevious()
    {
        FocusPreviousRequested?.Invoke(this);
    }

    public void RequestFocusNext()
    {
        FocusNextRequested?.Invoke(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

