using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI;

namespace Mnemo.UI.Components.BlockEditor;

public class BlockViewModel : INotifyPropertyChanged
{
    private string _id;
    private BlockType _type;
    private string _content;
    private Dictionary<string, object> _meta;
    private int _order;
    private int _listNumberIndex = 1;
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
            if (_type != value)
            {
                _type = value; 
                EnsureMetaKeys();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Watermark));
            }
        }
    }

    public string Content
    {
        get => _content;
        set 
        { 
            if (_content != value)
            {
                _previousContent = _content;
                _content = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Watermark));
                ContentChanged?.Invoke(this);
            }
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
        set => _order = value;
    }

    public int ListNumberIndex
    {
        get => _listNumberIndex;
        set
        {
            if (_listNumberIndex != value)
            {
                _listNumberIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ListNumber));
            }
        }
    }

    public string ListNumber => $"{_listNumberIndex}.";

    public bool IsFocused
    {
        get => _isFocused;
        set 
        { 
            if (_isFocused != value)
            {
                _isFocused = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(Watermark));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private int? _pendingCaretIndex;
    private string? _previousContent;

    /// <summary>
    /// When set, EditableBlock should move the caret to this index after the next focus.
    /// The consumer is responsible for clearing this value after use.
    /// </summary>
    public int? PendingCaretIndex
    {
        get => _pendingCaretIndex;
        set { _pendingCaretIndex = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Set by EditableBlock just before NotifyContentChanged to carry the pre-edit text.
    /// Consumed and cleared by the history system. Not persisted.
    /// </summary>
    public string? PreviousContent
    {
        get => _previousContent;
        set => _previousContent = value;
    }

    public bool IsChecked
    {
        get
        {
            if (!_meta.TryGetValue("checked", out var value)) return false;
            if (value is bool b) return b;
            if (value is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
            if (value is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
            return false;
        }
        set
        {
            NotifyStructuralChangeStarting();
            _meta["checked"] = value;
            OnPropertyChanged();
            ContentChanged?.Invoke(this);
        }
    }

    public string Watermark
    {
        get
        {
            var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
            string T(string key, string ns = "NotesEditor") => loc?.T(key, ns) ?? key;

            // Only show the slash command hint for empty, focused Text blocks
            if (Type == BlockType.Text && IsFocused && string.IsNullOrEmpty(Content))
                return T("TypeSlashForCommands");

            return Type switch
            {
                BlockType.Text => string.Empty,
                BlockType.Heading1 => T("Heading1"),
                BlockType.Heading2 => T("Heading2"),
                BlockType.Heading3 => T("Heading3"),
                BlockType.Quote => T("Quote"),
                BlockType.Code => T("Code"),
                BlockType.BulletList => T("ListItem"),
                BlockType.NumberedList => T("ListItem"),
                BlockType.Checklist => T("ChecklistItem"),
                _ => string.Empty
            };
        }
    }

    public event Action<BlockViewModel>? ContentChanged;
    public event Action<BlockViewModel>? DeleteRequested;
    public event Action<BlockViewModel, string?>? NewBlockRequested;
    public event Action<BlockViewModel, BlockType>? NewBlockOfTypeRequested;
    public event Action<BlockViewModel>? DeleteAndFocusAboveRequested;
    public event Action<BlockViewModel>? FocusPreviousRequested;
    public event Action<BlockViewModel>? FocusNextRequested;
    public event Action<BlockViewModel>? MergeWithPreviousRequested;
    /// <summary>
    /// Raised before a structural change (Enter split, backspace merge, type change)
    /// so the editor can snapshot the document while VMs are still unmodified.
    /// </summary>
    public event Action? StructuralChangeStarting;

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
        _id = string.IsNullOrEmpty(block.Id) ? Guid.NewGuid().ToString() : block.Id;
        _type = block.Type;
        _content = block.Content ?? string.Empty;
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

    public void NotifyStructuralChangeStarting()
    {
        StructuralChangeStarting?.Invoke();
    }

    public void RequestDelete()
    {
        DeleteRequested?.Invoke(this);
    }

    public void RequestNewBlock(string? initialContentForNewBlock = null)
    {
        NewBlockRequested?.Invoke(this, initialContentForNewBlock);
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

    public void RequestMergeWithPrevious()
    {
        MergeWithPreviousRequested?.Invoke(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


