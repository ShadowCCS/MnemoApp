using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
    private List<InlineSpan> _spans = new() { InlineSpan.Plain(string.Empty) };
    private string _cachedFlatContent = string.Empty;
    private Dictionary<string, object> _meta;
    private int _order;
    private int _listNumberIndex = 1;
    /// <summary>Fenced code language; canonical with <see cref="CodePayload"/> on persist. Not stored in <see cref="Meta"/>.</summary>
    private string _codeLanguage = "csharp";

    /// <summary>Standalone equation LaTeX; canonical with <see cref="EquationPayload"/> on persist. Not stored in <see cref="Meta"/>.</summary>
    private string _equationLatex = string.Empty;

    /// <summary>Checklist checked state; canonical with <see cref="ChecklistPayload"/> on persist. Not stored in <see cref="Meta"/>.</summary>
    private bool _checklistChecked;

    /// <summary>Image asset path; canonical with <see cref="ImagePayload.Path"/> on persist. Not stored in <see cref="Meta"/>.</summary>
    private string _imagePath = string.Empty;

    /// <summary>Image display width (0 = natural). Canonical with <see cref="ImagePayload.Width"/>.</summary>
    private double _imageWidth;

    /// <summary>Image horizontal alignment: left, center, right. Canonical with <see cref="ImagePayload.Align"/>.</summary>
    private string _imageAlign = "left";
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
                var prevType = _type;
                _type = value;
                if (value != BlockType.Checklist)
                    _checklistChecked = false;
                else if (prevType != BlockType.Checklist)
                {
                    _checklistChecked = ReadCheckedFromMeta(_meta);
                    if (_meta.Remove("checked"))
                        OnPropertyChanged(nameof(Meta));
                }

                if (value is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
                {
                    EnsureHeadingBold();
                    OnPropertyChanged(nameof(Content));
                    OnPropertyChanged(nameof(Spans));
                }
                else if (wasHeading)
                {
                    StripHeadingBoldFromRuns();
                }
                if (value == BlockType.Code && prevType != BlockType.Code)
                    _codeLanguage = "csharp";

                if (prevType == BlockType.Image && value != BlockType.Image)
                {
                    _imagePath = string.Empty;
                    _imageWidth = 0;
                    _imageAlign = "left";
                }

                if (value == BlockType.Image && prevType != BlockType.Image)
                {
                    _imagePath = ReadMetaString(_meta, "imagePath");
                    _imageWidth = ReadMetaDouble(_meta, "imageWidth");
                    _imageAlign = NormalizeImageAlign(ReadMetaString(_meta, "imageAlign"));
                    var legacyAlt = ReadMetaString(_meta, "imageAlt");
                    var metaChanged = false;
                    foreach (var k in new[] { "imagePath", "imageAlt", "imageWidth", "imageAlign" })
                        metaChanged |= _meta.Remove(k);
                    if (metaChanged)
                        OnPropertyChanged(nameof(Meta));
                    if (!string.IsNullOrEmpty(legacyAlt) && string.IsNullOrWhiteSpace(InlineSpanText.FlattenDisplay(_spans)))
                        SetSpans(new List<InlineSpan> { InlineSpan.Plain(legacyAlt) });
                }

                EnsureMetaKeys();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Watermark));
                if (value == BlockType.Code || prevType == BlockType.Code)
                    OnPropertyChanged(nameof(CodeLanguage));
                if (value == BlockType.Equation || prevType == BlockType.Equation)
                    OnPropertyChanged(nameof(EquationLatex));
                if (value == BlockType.Checklist || prevType == BlockType.Checklist)
                    OnPropertyChanged(nameof(IsChecked));
                if (value == BlockType.Image || prevType == BlockType.Image)
                {
                    OnPropertyChanged(nameof(ImagePath));
                    OnPropertyChanged(nameof(ImageWidth));
                    OnPropertyChanged(nameof(ImageAlign));
                }
            }
        }
    }

    /// <summary>Stored image file path for <see cref="BlockType.Image"/> blocks.</summary>
    public string ImagePath
    {
        get => _imagePath;
        set
        {
            var v = value ?? string.Empty;
            if (_imagePath == v) return;
            _imagePath = v;
            OnPropertyChanged();
            if (_type == BlockType.Image)
                ContentChanged?.Invoke(this);
        }
    }

    /// <summary>Display width in layout units; 0 means use natural size.</summary>
    public double ImageWidth
    {
        get => _imageWidth;
        set
        {
            if (Math.Abs(_imageWidth - value) < double.Epsilon) return;
            _imageWidth = value;
            OnPropertyChanged();
            if (_type == BlockType.Image)
                ContentChanged?.Invoke(this);
        }
    }

    /// <summary>Horizontal alignment for image layout: left, center, or right.</summary>
    public string ImageAlign
    {
        get => _imageAlign;
        set
        {
            var v = NormalizeImageAlign(value);
            if (_imageAlign == v) return;
            _imageAlign = v;
            OnPropertyChanged();
            if (_type == BlockType.Image)
                ContentChanged?.Invoke(this);
        }
    }

    private static string NormalizeImageAlign(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "center" => "center",
            "right" => "right",
            _ => "left",
        };

    /// <summary>Programming language id for fenced code blocks (e.g. csharp, python). Ignored when <see cref="Type"/> is not <see cref="BlockType.Code"/>.</summary>
    public string CodeLanguage
    {
        get => _codeLanguage;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "csharp" : value.Trim();
            if (_codeLanguage == v) return;
            _codeLanguage = v;
            OnPropertyChanged();
        }
    }

    /// <summary>LaTeX source for <see cref="BlockType.Equation"/> blocks. Ignored for other types.</summary>
    public string EquationLatex
    {
        get => _equationLatex;
        set
        {
            var v = value ?? string.Empty;
            if (_equationLatex == v) return;
            _equationLatex = v;
            OnPropertyChanged();
            if (_type == BlockType.Equation)
                ContentChanged?.Invoke(this);
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
            _previousSpans = CloneSpans();
            _spans = InlineSpanFormatApplier.ApplyTextEdit(_spans, _cachedFlatContent, newText);
            EnsureHeadingBold();
            if (_type != BlockType.Code)
            {
                var autolinked = InlineSpanFormatApplier.Normalize(InlineAutoLink.Apply(_spans));
                if (!SpansListContentEqual(_spans, autolinked))
                {
                    _spans = autolinked;
                    EnsureHeadingBold();
                }
            }

            _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Watermark));
            OnPropertyChanged(nameof(Spans));
            ContentChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// The structured inline runs (source of truth for rich text).
    /// </summary>
    public IReadOnlyList<InlineSpan> Spans => _spans;

    private void EnsureHeadingBold()
    {
        if (_type is not (BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3))
            return;
        var boldRuns = _spans
            .Select(s => s is TextSpan t ? t with { Style = t.Style.WithSet(InlineFormatKind.Bold) } : s)
            .ToList();
        _spans = InlineSpanFormatApplier.Normalize(boldRuns);
        if (_spans.Count == 0)
            _spans.Add(new TextSpan(string.Empty, new TextStyle(Bold: true)));
    }

    /// <summary>Removes bold from all runs when leaving a heading block (e.g. converting to plain text).</summary>
    private void StripHeadingBoldFromRuns()
    {
        _spans = InlineSpanFormatApplier.Normalize(
            _spans.Select(s => s is TextSpan t ? t with { Style = t.Style.WithClear(InlineFormatKind.Bold) } : s).ToList());
        if (_spans.Count == 0)
            _spans.Add(InlineSpan.Plain(string.Empty));
        _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Spans));
        ContentChanged?.Invoke(this);
    }

    /// <summary>
    /// Replace the entire run list (e.g. for undo/redo or deserialization).
    /// Normalizes and refreshes Content. Does not raise ContentChanged.
    /// </summary>
    public void SetSpans(IReadOnlyList<InlineSpan> runs)
    {
        _spans = InlineSpanFormatApplier.Normalize(runs);
        if (_spans.Count == 0)
            _spans.Add(InlineSpan.Plain(string.Empty));
        EnsureHeadingBold();
        _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Spans));
        OnPropertyChanged(nameof(Watermark));
    }

    /// <summary>
    /// Commit runs from the editor: capture previous content for history, update runs, then raise ContentChanged.
    /// Use this for user edits (typing, paste, delete selection); use SetSpans for restore/undo.
    /// </summary>
    public void CommitSpansFromEditor(IReadOnlyList<InlineSpan> newRuns)
    {
        _previousContent = _cachedFlatContent;
        _previousSpans = CloneSpans();
        SetSpans(newRuns);
        if (_type != BlockType.Code)
        {
            var autolinked = InlineSpanFormatApplier.Normalize(InlineAutoLink.Apply(_spans));
            if (!SpansListContentEqual(_spans, autolinked))
            {
                _spans = autolinked;
                EnsureHeadingBold();
                _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);
                OnPropertyChanged(nameof(Content));
                OnPropertyChanged(nameof(Spans));
                OnPropertyChanged(nameof(Watermark));
            }
        }

        ContentChanged?.Invoke(this);
    }

    private static bool SpansListContentEqual(IReadOnlyList<InlineSpan> a, IReadOnlyList<InlineSpan> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            switch (a[i], b[i])
            {
                case (TextSpan ta, TextSpan tb):
                    if (ta.Text != tb.Text || ta.Style != tb.Style) return false;
                    break;
                case (EquationSpan ea, EquationSpan eb):
                    if (ea.Latex != eb.Latex || ea.Style != eb.Style) return false;
                    break;
                case (FractionSpan fa, FractionSpan fb):
                    if (fa.Numerator != fb.Numerator || fa.Denominator != fb.Denominator || fa.Style != fb.Style) return false;
                    break;
                default:
                    return false;
            }
        }

        return true;
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
        _previousSpans = CloneSpans();
        _spans = InlineSpanFormatApplier.Apply(_spans, start, end, kind, color);
        _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Spans));
        ContentChanged?.Invoke(this);
        return (start, end);
    }

    /// <summary>
    /// Sets or clears a link on <c>[start, end)</c>, optionally replacing that range with <paramref name="displayText"/> first.
    /// When <paramref name="removeLink"/> is true, clears link formatting on the range (ignores <paramref name="url"/>).
    /// </summary>
    public (int Start, int End) ApplyLinkEdit(int start, int end, string displayText, string url, bool removeLink)
    {
        _previousContent = _cachedFlatContent;
        _previousSpans = CloneSpans();

        if (removeLink)
        {
            _spans = InlineSpanFormatApplier.Apply(_spans, start, end, InlineFormatKind.Link, null);
            _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);
            OnPropertyChanged(nameof(Content));
            OnPropertyChanged(nameof(Spans));
            ContentChanged?.Invoke(this);
            return (start, end);
        }

        var flat = InlineSpanFormatApplier.Flatten(_spans);
        if (start < 0 || end > flat.Length || start > end)
            return (start, end);

        if (end > start)
        {
            var slice = flat.Substring(start, end - start);
            if (slice != displayText)
            {
                var newFlat = flat.Substring(0, start) + displayText + flat.Substring(end);
                _spans = InlineSpanFormatApplier.ApplyTextEdit(_spans, flat, newFlat);
            }
        }

        flat = InlineSpanFormatApplier.Flatten(_spans);
        int linkEnd = start + displayText.Length;
        if (linkEnd > flat.Length) linkEnd = flat.Length;
        if (linkEnd <= start)
        {
            OnPropertyChanged(nameof(Content));
            OnPropertyChanged(nameof(Spans));
            ContentChanged?.Invoke(this);
            return (start, start);
        }

        _spans = InlineSpanFormatApplier.Apply(_spans, start, linkEnd, InlineFormatKind.Link, url);
        _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Spans));
        ContentChanged?.Invoke(this);
        return (start, linkEnd);
    }

    /// <summary>
    /// Deep-copy the current runs for snapshotting (undo/redo).
    /// </summary>
    public List<InlineSpan> CloneSpans() => new(_spans);

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
            if (_isFocused == value)
                return;

            _isFocused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Watermark));
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
    private List<InlineSpan>? _previousSpans;

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
    public List<InlineSpan>? PreviousSpans
    {
        get => _previousSpans;
        set => _previousSpans = value;
    }

    public bool IsChecked
    {
        get => _type == BlockType.Checklist && _checklistChecked;
        set
        {
            if (_type != BlockType.Checklist || _checklistChecked == value)
                return;
            NotifyStructuralChangeStarting();
            _checklistChecked = value;
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

    /// <summary>Left column width fraction (0.1–0.9). Legacy: stored on the left block of a flat pair in <see cref="Meta"/>; nested splits use <see cref="TwoColumnBlockViewModel"/>.</summary>
    public virtual double ColumnSplitRatio
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
        var defaultStyle = type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 ? new TextStyle(Bold: true) : TextStyle.Default;
        _spans = new List<InlineSpan> { new TextSpan(content ?? string.Empty, defaultStyle) };
        _cachedFlatContent = content ?? string.Empty;
        _meta = new Dictionary<string, object>();
        _order = order;
        if (type == BlockType.Code)
            _codeLanguage = "csharp";

        EnsureMetaKeys();
    }

    public BlockViewModel(Block block)
    {
        _id = string.IsNullOrEmpty(block.Id) ? Guid.NewGuid().ToString() : block.Id;
        _type = block.Type;
        _meta = new Dictionary<string, object>(block.Meta ?? new Dictionary<string, object>());
        _order = block.Order;

        ApplyPayloadFieldsToMeta(block.Payload, _meta);
        InitImageFromBlock(block);

        block.EnsureSpans();
        _spans = InlineSpanFormatApplier.Normalize(block.Spans);
        if (_spans.Count == 0)
            _spans.Add(InlineSpan.Plain(string.Empty));
        EnsureHeadingBold();
        _cachedFlatContent = InlineSpanFormatApplier.Flatten(_spans);

        EnsureMetaKeys();

        if (_type == BlockType.Image
            && string.IsNullOrWhiteSpace(_cachedFlatContent)
            && block.Payload is ImagePayload ipSeed
            && !string.IsNullOrWhiteSpace(ipSeed.Alt))
            SetSpans(new List<InlineSpan> { InlineSpan.Plain(ipSeed.Alt) });

        if (_type == BlockType.Code)
            InitCodeLanguageFromBlock(block);

        if (_type == BlockType.Equation)
            InitEquationFromBlock(block);

        if (_type == BlockType.Checklist)
            InitChecklistFromBlock(block);
    }

    private static string ReadMetaString(Dictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var val) || val == null) return string.Empty;
        if (val is string s) return s;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return val.ToString() ?? string.Empty;
    }

    private static void ApplyPayloadFieldsToMeta(BlockPayload payload, Dictionary<string, object> meta)
    {
        switch (payload)
        {
            case EmptyPayload:
                break;
            case EquationPayload:
                break;
            case ImagePayload:
                break;
            case CodePayload:
                break;
            case ChecklistPayload:
                break;
            case TwoColumnPayload:
                break;
            default:
                throw new UnreachableException($"Unexpected block payload type: {payload.GetType().Name}");
        }
    }

    /// <summary>Removes flat-pair column keys from a cell once it is wired under a <see cref="TwoColumnBlockViewModel"/>.</summary>
    internal void StripColumnLayoutMetaAsNestedCell()
    {
        var changed = false;
        if (_meta.Remove(ColumnPairHelper.PairIdKey)) changed = true;
        if (_meta.Remove(ColumnPairHelper.SideKey)) changed = true;
        if (_meta.Remove("columnSplitRatio")) changed = true;
        if (changed)
            OnPropertyChanged(nameof(Meta));
    }

    private static bool ReadCheckedFromMeta(Dictionary<string, object> meta)
    {
        if (!meta.TryGetValue("checked", out var value) || value == null)
            return false;
        if (value is bool b)
            return b;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.True)
            return true;
        return false;
    }

    private void InitCodeLanguageFromBlock(Block block)
    {
        if (block.Payload is CodePayload cp)
            _codeLanguage = string.IsNullOrWhiteSpace(cp.Language) ? "csharp" : cp.Language.Trim();
        else
            _codeLanguage = string.IsNullOrWhiteSpace(ReadMetaString(_meta, "language")) ? "csharp" : ReadMetaString(_meta, "language");
        if (_meta.Remove("language"))
            OnPropertyChanged(nameof(Meta));
    }

    private void InitEquationFromBlock(Block block)
    {
        if (block.Payload is EquationPayload ep)
            _equationLatex = ep.Latex ?? string.Empty;
        else
            _equationLatex = ReadMetaString(_meta, "equationLatex");
        if (_meta.Remove("equationLatex"))
            OnPropertyChanged(nameof(Meta));
    }

    private void InitChecklistFromBlock(Block block)
    {
        if (block.Payload is ChecklistPayload cp)
            _checklistChecked = cp.Checked;
        else
            _checklistChecked = ReadCheckedFromMeta(_meta);
        if (_meta.Remove("checked"))
            OnPropertyChanged(nameof(Meta));
    }

    private void InitImageFromBlock(Block block)
    {
        if (block.Type != BlockType.Image)
            return;

        string path;
        double width;
        string align;
        if (block.Payload is ImagePayload ip)
        {
            path = ip.Path ?? string.Empty;
            width = ip.Width;
            align = string.IsNullOrWhiteSpace(ip.Align) ? "left" : ip.Align.Trim();
        }
        else
        {
            path = ReadMetaString(_meta, "imagePath");
            width = ReadMetaDouble(_meta, "imageWidth");
            align = ReadMetaString(_meta, "imageAlign");
        }

        var metaChanged = false;
        foreach (var k in new[] { "imagePath", "imageAlt", "imageWidth", "imageAlign" })
            metaChanged |= _meta.Remove(k);

        _imagePath = path;
        _imageWidth = width;
        _imageAlign = NormalizeImageAlign(align);
        if (metaChanged)
            OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageAlign));
    }

    private static double ReadMetaDouble(Dictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v) || v == null) return 0;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is JsonElement je && je.TryGetDouble(out var x)) return x;
        return 0;
    }

    private BlockPayload BuildPayloadForPersistence()
    {
        return Type switch
        {
            BlockType.Equation => new EquationPayload(_equationLatex),
            BlockType.Checklist => new ChecklistPayload(_checklistChecked),
            BlockType.Code => new CodePayload(_codeLanguage, InlineSpanText.FlattenDisplay(_spans)),
            BlockType.Image => new ImagePayload(
                _imagePath,
                InlineSpanText.FlattenDisplay(_spans),
                _imageWidth,
                _imageAlign),
            _ => new EmptyPayload()
        };
    }
    
    private void EnsureMetaKeys()
    {
        bool metaChanged = false;

        if (_type != BlockType.Image)
        {
            foreach (var k in new[] { "imagePath", "imageAlt", "imageWidth", "imageAlign" })
            {
                if (_meta.Remove(k))
                    metaChanged = true;
            }
        }

        if (_type != BlockType.Code && _meta.Remove("language"))
            metaChanged = true;

        if (_type != BlockType.Equation && _meta.Remove("equationLatex"))
            metaChanged = true;

        if (_type != BlockType.Checklist && _meta.Remove("checked"))
            metaChanged = true;

        if (OwnerTwoColumn != null)
        {
            if (_meta.Remove(ColumnPairHelper.PairIdKey)) metaChanged = true;
            if (_meta.Remove(ColumnPairHelper.SideKey)) metaChanged = true;
            if (_meta.Remove("columnSplitRatio")) metaChanged = true;
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
            Spans = new List<InlineSpan>(_spans),
            Payload = BuildPayloadForPersistence(),
            Meta = new Dictionary<string, object>(Meta),
            Order = Order
        };
        if (Type == BlockType.Code)
            block.Meta.Remove("language");
        if (Type == BlockType.Equation)
            block.Meta.Remove("equationLatex");
        if (Type == BlockType.Checklist)
            block.Meta.Remove("checked");
        if (Type == BlockType.Image)
        {
            foreach (var k in new[] { "imagePath", "imageAlt", "imageWidth", "imageAlign" })
                block.Meta.Remove(k);
        }

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