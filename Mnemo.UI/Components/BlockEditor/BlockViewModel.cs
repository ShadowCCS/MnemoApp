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

    /// <summary>Primary block (handle source) plus all blocks to move together (document order).</summary>
    public sealed class BlockReorderDragPayload
    {
        public required BlockViewModel Primary { get; init; }
        public required IReadOnlyList<BlockViewModel> BlocksInDocumentOrder { get; init; }

        public static readonly DataFormat<BlockReorderDragPayload> Format =
            AvaloniaDataFormats.CreateApplicationFormat<BlockReorderDragPayload>("BlockReorderDragPayload");
    }

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
                var wasHeading = _type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3;
                _type = value;
                if (value is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
                {
                    EnsureHeadingBold();
                    OnPropertyChanged(nameof(Content));
                    OnPropertyChanged(nameof(Runs));
                }
                else if (wasHeading)
                {
                    StripHeadingBoldFromRuns();
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

    /// <summary>Removes bold from all runs when leaving a heading block (e.g. converting to plain text).</summary>
    private void StripHeadingBoldFromRuns()
    {
        _inlineRuns = InlineRunFormatApplier.Normalize(
            _inlineRuns.Select(r => new InlineRun(r.Text, r.Style.WithClear(InlineFormatKind.Bold))).ToList());
        if (_inlineRuns.Count == 0)
            _inlineRuns.Add(InlineRun.Plain(string.Empty));
        _cachedFlatContent = InlineRunFormatApplier.Flatten(_inlineRuns);
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Runs));
        ContentChanged?.Invoke(this);
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
        if (_type == BlockType.Image)
        {
            _meta["imageAlt"] = _cachedFlatContent ?? string.Empty;
            OnPropertyChanged(nameof(Meta));
        }
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
        if (kind == InlineFormatKind.Bold && _type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
            return (start, end);

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

    /// <summary>When set with <see cref="PendingCaretPlaceOnLastLine"/>, positions the caret by horizontal pixel column (see <see cref="RichTextEditor.GetCaretIndexFromHorizontalOffset"/>).</summary>
    public double? PendingCaretPixelX { get; set; }

    /// <summary>True: Up into this block — use last visual line. False: Down into this block — first line.</summary>
    public bool PendingCaretPlaceOnLastLine { get; set; }

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

            // Placeholders only on the empty block that has keyboard focus (avoids ghost watermarks when adding blocks).
            if (!IsFocused || !BlockEditorContentPolicy.IsVisuallyEmpty(Content))
                return string.Empty;

            return Type switch
            {
                BlockType.Text => T("TypeSlashForCommands"),
                BlockType.Heading1 => T("Heading1"),
                BlockType.Heading2 => T("Heading2"),
                BlockType.Heading3 => T("Heading3"),
                BlockType.Quote => T("Quote"),
                BlockType.Code => T("Code"),
                BlockType.BulletList => T("ListItem"),
                BlockType.NumberedList => T("ListItem"),
                BlockType.Checklist => T("ChecklistItem"),
                BlockType.Image => string.Empty,
                _ => string.Empty
            };
        }
    }

    /// <summary>Left column width fraction (0.1–0.9) for the left block of a column pair (see <see cref="ColumnPairHelper"/>).</summary>
    public double ColumnSplitRatio
    {
        get
        {
            if (_meta.TryGetValue("columnSplitRatio", out var v))
            {
                if (v is double d) return Math.Clamp(d, 0.1, 0.9);
                if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
                    return Math.Clamp(je.GetDouble(), 0.1, 0.9);
            }
            return 0.5;
        }
        set
        {
            var r = Math.Clamp(value, 0.1, 0.9);
            _meta["columnSplitRatio"] = r;
            OnPropertyChanged();
        }
    }

    /// <summary>Other block in the same side-by-side pair, if any (uses <c>columnPairId</c> meta).</summary>
    public BlockViewModel? GetColumnSibling(IReadOnlyList<BlockViewModel> document) =>
        ColumnPairHelper.GetSibling(this, document);

    public event Action<BlockViewModel>? ContentChanged;
    public event Action<BlockViewModel>? DeleteRequested;
    /// <summary>Duplicate this block (e.g. image: copy asset into a new block below).</summary>
    public event Action<BlockViewModel>? DuplicateBlockRequested;
    public event Action<BlockViewModel, string?>? NewBlockRequested;
    /// <summary>Third argument is initial plain text for the new block (e.g. Enter split).</summary>
    public event Action<BlockViewModel, BlockType, string?>? NewBlockOfTypeRequested;
    public event Action<BlockViewModel, string?>? NewBlockAboveRequested;
    public event Action<BlockViewModel>? DeleteAndFocusAboveRequested;
    public event Action<BlockViewModel, double?>? FocusPreviousRequested;
    public event Action<BlockViewModel, double?>? FocusNextRequested;
    public event Action<BlockViewModel>? MergeWithPreviousRequested;
    /// <summary>Empty line in a split column: new block below the split (like quote exit).</summary>
    public event Action<BlockViewModel, string?>? ExitSplitBelowRequested;
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

        if (_type == BlockType.Image)
        {
            var alt = ReadMetaString(_meta, "imageAlt");
            SetRuns(new List<InlineRun> { InlineRun.Plain(alt) });
        }
    }

    private static string ReadMetaString(Dictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var val) || val == null) return string.Empty;
        if (val is string s) return s;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return val.ToString() ?? string.Empty;
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
            case BlockType.Image:
                if (!_meta.ContainsKey("imagePath"))
                {
                    _meta["imagePath"] = string.Empty;
                    metaChanged = true;
                }
                if (!_meta.ContainsKey("imageAlt"))
                {
                    _meta["imageAlt"] = string.Empty;
                    metaChanged = true;
                }
                if (!_meta.ContainsKey("imageWidth"))
                {
                    _meta["imageWidth"] = 0.0;
                    metaChanged = true;
                }
                if (!_meta.ContainsKey("imageAlign"))
                {
                    _meta["imageAlign"] = "left";
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

    /// <summary>When non-null, this block lives inside a <see cref="TwoColumnBlockViewModel"/> column (not a top-level row).</summary>
    public TwoColumnBlockViewModel? OwnerTwoColumn { get; internal set; }

    /// <summary>Which column of <see cref="OwnerTwoColumn"/> this block belongs to.</summary>
    public bool IsLeftColumn { get; internal set; }

    public virtual Block ToBlock()
    {
        var block = new Block
        {
            Id = Id,
            Type = Type,
            InlineRuns = new List<InlineRun>(_inlineRuns),
            Meta = Meta,
            Order = Order
        };
        return block;
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

    public void RequestDuplicateBlock()
    {
        DuplicateBlockRequested?.Invoke(this);
    }

    public void RequestNewBlock(string? initialContentForNewBlock = null)
    {
        NewBlockRequested?.Invoke(this, initialContentForNewBlock);
    }

    public void RequestNewBlockOfType(BlockType type, string? initialContentForNewBlock = null)
    {
        NewBlockOfTypeRequested?.Invoke(this, type, initialContentForNewBlock);
    }

    /// <summary>Inserts an empty text block above this block (e.g. Enter at start of line).</summary>
    public void RequestNewBlockAbove(string? initialContentForNewBlock = null)
    {
        NewBlockAboveRequested?.Invoke(this, initialContentForNewBlock);
    }

    public void RequestDeleteAndFocusAbove()
    {
        DeleteAndFocusAboveRequested?.Invoke(this);
    }

    public void RequestFocusPrevious(double? caretPixelX = null)
    {
        FocusPreviousRequested?.Invoke(this, caretPixelX);
    }

    public void RequestFocusNext(double? caretPixelX = null)
    {
        FocusNextRequested?.Invoke(this, caretPixelX);
    }

    public void RequestMergeWithPrevious()
    {
        MergeWithPreviousRequested?.Invoke(this);
    }

    public void RequestExitSplitBelow(string? followingText)
    {
        ExitSplitBelowRequested?.Invoke(this, followingText);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}