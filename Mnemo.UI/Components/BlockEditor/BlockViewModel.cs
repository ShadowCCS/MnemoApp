using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Input;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI;
using Mnemo.UI.Input;

namespace Mnemo.UI.Components.BlockEditor;

public class BlockViewModel : INotifyPropertyChanged
{
    /// <summary>Drag-and-drop format for reordering blocks in the editor.</summary>
    public static readonly DataFormat<BlockViewModel> BlockDragDataFormat =
        AvaloniaDataFormats.CreateApplicationFormat<BlockViewModel>("BlockViewModel");

    private string _id;
    private BlockType _type;
    private List<InlineRun> _inlineRuns = new() { InlineRun.Plain(string.Empty) };
    private string _cachedFlatContent = string.Empty;
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
                if (value is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
                {
                    EnsureHeadingBold();
                    OnPropertyChanged(nameof(Content));
                    OnPropertyChanged(nameof(Runs));
                }
                EnsureMetaKeys();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Watermark));
            }
        }
    }

    /// <summary>
    /// Flattened plain text view of the inline runs.
    /// Setting this applies a text diff to the run list, preserving styles outside the edit region.
    /// </summary>
    public string Content
    {
        get => _cachedFlatContent;
        set
        {
            var newText = value ?? string.Empty;
            if (_cachedFlatContent == newText)
                return;

            _previousContent = _cachedFlatContent;
            _previousRuns = CloneRuns();
            _inlineRuns = InlineRunFormatApplier.ApplyTextEdit(_inlineRuns, _cachedFlatContent, newText);
            EnsureHeadingBold();
            _cachedFlatContent = InlineRunFormatApplier.Flatten(_inlineRuns);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Watermark));
            OnPropertyChanged(nameof(Runs));
            ContentChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// The structured inline runs (source of truth for rich text).
    /// </summary>
    public IReadOnlyList<InlineRun> Runs => _inlineRuns;

    private void EnsureHeadingBold()
    {
        if (_type is not (BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3))
            return;
        var boldRuns = _inlineRuns.Select(r => new InlineRun(r.Text, r.Style.WithSet(InlineFormatKind.Bold))).ToList();
        _inlineRuns = InlineRunFormatApplier.Normalize(boldRuns);
        if (_inlineRuns.Count == 0)
            _inlineRuns.Add(new InlineRun(string.Empty, new InlineStyle(Bold: true)));
    }

    /// <summary>
    /// Replace the entire run list (e.g. for undo/redo or deserialization).
    /// Normalizes and refreshes Content. Does not raise ContentChanged.
    /// </summary>
    public void SetRuns(IReadOnlyList<InlineRun> runs)
    {
        _inlineRuns = InlineRunFormatApplier.Normalize(runs);
        if (_inlineRuns.Count == 0)
            _inlineRuns.Add(InlineRun.Plain(string.Empty));
        EnsureHeadingBold();
        _cachedFlatContent = InlineRunFormatApplier.Flatten(_inlineRuns);
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Runs));
        OnPropertyChanged(nameof(Watermark));
    }

    /// <summary>
    /// Commit runs from the editor: capture previous content for history, update runs, then raise ContentChanged.
    /// Use this for user edits (typing, paste, delete selection); use SetRuns for restore/undo.
    /// </summary>
    public void CommitRunsFromEditor(IReadOnlyList<InlineRun> newRuns)
    {
        _previousContent = _cachedFlatContent;
        _previousRuns = CloneRuns();
        SetRuns(newRuns);
        ContentChanged?.Invoke(this);
    }

    /// <summary>
    /// Apply a format toggle to the selection range.
    /// Returns the (unchanged) selection for the caller to restore on the TextBox.
    /// </summary>
    public (int Start, int End) ApplyFormat(int start, int end, InlineFormatKind kind, string? color = null)
    {
        _previousContent = _cachedFlatContent;
        _previousRuns = CloneRuns();
        _inlineRuns = InlineRunFormatApplier.Apply(_inlineRuns, start, end, kind, color);
        _cachedFlatContent = InlineRunFormatApplier.Flatten(_inlineRuns);
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Runs));
        ContentChanged?.Invoke(this);
        return (start, end);
    }

    /// <summary>
    /// Deep-copy the current runs for snapshotting (undo/redo).
    /// </summary>
    public List<InlineRun> CloneRuns() => new(_inlineRuns);

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
    private List<InlineRun>? _previousRuns;

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

    /// <summary>
    /// Set alongside PreviousContent to carry the pre-edit formatting runs.
    /// </summary>
    public List<InlineRun>? PreviousRuns
    {
        get => _previousRuns;
        set => _previousRuns = value;
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
        var defaultStyle = type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 ? new InlineStyle(Bold: true) : InlineStyle.Default;
        _inlineRuns = new List<InlineRun> { new InlineRun(content ?? string.Empty, defaultStyle) };
        _cachedFlatContent = content ?? string.Empty;
        _meta = new Dictionary<string, object>();
        _order = order;
        
        EnsureMetaKeys();
    }

    public BlockViewModel(Block block)
    {
        _id = string.IsNullOrEmpty(block.Id) ? Guid.NewGuid().ToString() : block.Id;
        _type = block.Type;
        _meta = new Dictionary<string, object>(block.Meta ?? new Dictionary<string, object>());
        _order = block.Order;

        block.EnsureInlineRuns();
        _inlineRuns = InlineRunFormatApplier.Normalize(block.InlineRuns!);
        if (_inlineRuns.Count == 0)
            _inlineRuns.Add(InlineRun.Plain(string.Empty));
        EnsureHeadingBold();
        _cachedFlatContent = InlineRunFormatApplier.Flatten(_inlineRuns);
        
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
            InlineRuns = new List<InlineRun>(_inlineRuns),
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


