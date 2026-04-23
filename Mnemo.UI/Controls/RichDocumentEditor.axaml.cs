using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Formatting;
using Mnemo.Core.History;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Notes.Markdown;
using Mnemo.UI.Components.BlockEditor;
using Mnemo.UI.Components.Overlays;

namespace Mnemo.UI.Controls;

/// <summary>
/// Reusable rich-document editor with a fixed toolbar and preview mode.
/// Images are managed as a separate layer from inline text: the inner
/// <see cref="RichTextEditor"/> only ever sees text-only spans, while image
/// metadata lives in <see cref="_imageEntries"/> and is re-injected into
/// <see cref="Spans"/> (the persisted/preview payload) transparently.
/// </summary>
public partial class RichDocumentEditor : UserControl, INotifyPropertyChanged
{
    private static readonly Regex EmbeddedImageRegex = new(
        @"!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)(?:\{align=(?<align>left|center|right)\})?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public new event PropertyChangedEventHandler? PropertyChanged;
    public event Action<IReadOnlyList<InlineSpan>>? SpansChanged;

    public static readonly StyledProperty<IReadOnlyList<InlineSpan>> SpansProperty =
        AvaloniaProperty.Register<RichDocumentEditor, IReadOnlyList<InlineSpan>>(
            nameof(Spans),
            defaultValue: new List<InlineSpan> { InlineSpan.Plain(string.Empty) },
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<RichDocumentEditor, string?>(nameof(Watermark));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<RichDocumentEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> IsPreviewModeProperty =
        AvaloniaProperty.Register<RichDocumentEditor, bool>(nameof(IsPreviewMode));

    public static readonly StyledProperty<bool> ShowModeToggleProperty =
        AvaloniaProperty.Register<RichDocumentEditor, bool>(nameof(ShowModeToggle), defaultValue: true);

    public static readonly StyledProperty<bool> ShowImageButtonProperty =
        AvaloniaProperty.Register<RichDocumentEditor, bool>(nameof(ShowImageButton), defaultValue: true);

    public static readonly StyledProperty<string> WriteLabelProperty =
        AvaloniaProperty.Register<RichDocumentEditor, string>(nameof(WriteLabel), defaultValue: "Write");

    public static readonly StyledProperty<string> PreviewLabelProperty =
        AvaloniaProperty.Register<RichDocumentEditor, string>(nameof(PreviewLabel), defaultValue: "Preview");

    public IReadOnlyList<InlineSpan> Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool IsPreviewMode
    {
        get => GetValue(IsPreviewModeProperty);
        set => SetValue(IsPreviewModeProperty, value);
    }

    public bool ShowModeToggle
    {
        get => GetValue(ShowModeToggleProperty);
        set => SetValue(ShowModeToggleProperty, value);
    }

    public bool ShowImageButton
    {
        get => GetValue(ShowImageButtonProperty);
        set => SetValue(ShowImageButtonProperty, value);
    }

    public string WriteLabel
    {
        get => GetValue(WriteLabelProperty);
        set => SetValue(WriteLabelProperty, value);
    }

    public string PreviewLabel
    {
        get => GetValue(PreviewLabelProperty);
        set => SetValue(PreviewLabelProperty, value);
    }

    public bool IsToolbarEnabled => !IsReadOnly && !IsPreviewMode;

    public bool CanInsertImage { get; private set; } = true;

    public string PreviewMarkdown { get; private set; } = string.Empty;

    // ── Image layer ─────────────────────────────────────────────────────────
    // Source of truth for images: populated from Spans on load, updated by
    // image operations, and re-serialised back into Spans by BuildCombinedSpans.

    private readonly List<(string Path, string Align)> _imageEntries = new();
    private readonly List<RichDocumentEmbeddedImageViewItem> _embeddedImages = new();
    private int _currentImageIndex;

    /// <summary>Currently displayed image in the carousel, or null when there are none.</summary>
    public RichDocumentEmbeddedImageViewItem? CurrentImage =>
        _embeddedImages.Count > 0
            ? _embeddedImages[Math.Clamp(_currentImageIndex, 0, _embeddedImages.Count - 1)]
            : null;

    public string? CurrentImagePath => CurrentImage?.Path;
    public bool IsCurrentImageAlignLeft => CurrentImage?.IsAlignLeft == true;
    public bool IsCurrentImageAlignCenter => CurrentImage?.IsAlignCenter == true;
    public bool IsCurrentImageAlignRight => CurrentImage?.IsAlignRight == true;

    public bool HasImages => _embeddedImages.Count > 0;
    public bool HasMultipleImages => _embeddedImages.Count > 1;
    public bool CanGoToPreviousImage => _currentImageIndex > 0 && HasImages;
    public bool CanGoToNextImage => _currentImageIndex < _embeddedImages.Count - 1;
    public string ImageCountLabel => HasMultipleImages ? $"{_currentImageIndex + 1} / {_embeddedImages.Count}" : string.Empty;

    // Legacy surface kept for any external consumers.
    public IReadOnlyList<RichDocumentEmbeddedImageViewItem> EmbeddedImages => _embeddedImages;
    public bool HasEmbeddedImages => _embeddedImages.Count > 0;

    // ── Internal state ───────────────────────────────────────────────────────

    private bool _isSyncingFromProperty;
    private bool _isHandlingEditorTextChanged;
    private bool _isApplyingHistory;
    private IHistoryManager? _history;
    private EditorSnapshot? _historyBaseline;
    private readonly IOverlayService? _overlayService;
    private readonly ILocalizationService? _localization;
    private readonly IImageAssetService? _imageAssetService;

    public RichDocumentEditor()
    {
        InitializeComponent();
        var services = (Application.Current as App)?.Services;
        _overlayService = services?.GetService<IOverlayService>();
        _localization = services?.GetService<ILocalizationService>();
        _imageAssetService = services?.GetService<IImageAssetService>();
        _history = services?.GetService<IHistoryManager>();
        UpdatePreviewMarkdown();
        SyncImageViewItems();
        UpdateModeButtonClasses();
        _historyBaseline = CaptureSnapshot();
    }

    /// <summary>
    /// Optional document-scoped history manager. When attached, the editor records
    /// text and image changes so Ctrl+Z / Ctrl+Y can undo and redo.
    /// </summary>
    public IHistoryManager? History
    {
        get => _history;
        set => _history = value;
    }

    /// <summary>
    /// Convenience API for host views that attach history manager imperatively.
    /// </summary>
    public void AttachHistoryManager(IHistoryManager? historyManager) => History = historyManager;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateModeButtonClasses();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SpansProperty)
        {
            if (!_isSyncingFromProperty)
            {
                ExtractImageEntries(Spans);
                var textOnly = BuildTextOnlySpans(Spans);
                _isSyncingFromProperty = true;
                try { Editor.Spans = textOnly; }
                finally { _isSyncingFromProperty = false; }
                UpdatePreviewMarkdown();
                SyncImageViewItems();
            }
        }
        else if (change.Property == IsPreviewModeProperty || change.Property == IsReadOnlyProperty)
        {
            RaisePropertyChanged(nameof(IsToolbarEnabled));
            RefreshCanInsertImage();
            UpdateModeButtonClasses();
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public bool TryWrapSelectionWithCloze(int ordinal)
    {
        if (IsReadOnly || IsPreviewMode || ordinal <= 0)
            return false;
        var start = Math.Min(Editor.SelectionStart, Editor.SelectionEnd);
        var end = Math.Max(Editor.SelectionStart, Editor.SelectionEnd);
        if (start < 0 || end < start)
            return false;

        var selectedText = end > start && Editor.Text.Length >= end
            ? Editor.Text[start..end]
            : string.Empty;
        var wrapped = selectedText.Length > 0
            ? $"{{{{c{ordinal}::{selectedText}}}}}"
            : $"{{{{c{ordinal}::}}}}";

        Editor.InsertTextAtCaret(wrapped);
        if (selectedText.Length == 0)
        {
            var insideCaret = Math.Max(0, Editor.CaretIndex - 2);
            Editor.CaretIndex = insideCaret;
            Editor.SelectionStart = insideCaret;
            Editor.SelectionEnd = insideCaret;
        }

        return true;
    }

    public void LoadSpans(IReadOnlyList<InlineSpan> spans)
    {
        _isSyncingFromProperty = true;
        try
        {
            var normalized = InlineSpanFormatApplier.Normalize(
                spans ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) });

            ExtractImageEntries(normalized);
            var textOnly = BuildTextOnlySpans(normalized);

            Spans = normalized;
            Editor.Spans = textOnly;

            UpdatePreviewMarkdown();
            SyncImageViewItems();
            _historyBaseline = CaptureSnapshot();
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    // ── Editor events ────────────────────────────────────────────────────────

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isSyncingFromProperty || _isHandlingEditorTextChanged)
            return;

        var editorSpans = InlineSpanFormatApplier.Normalize(
            Editor.Spans ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) });

        // Absorb any image tokens the user pasted into the text editor.
        var editorFlat = InlineSpanFormatApplier.Flatten(editorSpans);
        var imageMatches = EmbeddedImageRegex.Matches(editorFlat);
        if (imageMatches.Count > 0)
        {
            foreach (Match m in imageMatches)
            {
                var path = m.Groups["path"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(path))
                    _imageEntries.Add((path, NormalizeAlign(m.Groups["align"].Value)));
            }

            var stripped = StripImageTokensFromText(editorFlat);
            _isSyncingFromProperty = true;
            try
            {
                editorSpans = string.Equals(stripped, editorFlat, StringComparison.Ordinal)
                    ? editorSpans
                    : InlineSpanFormatApplier.Normalize(
                        InlineSpanFormatApplier.ApplyTextEdit(editorSpans, editorFlat, stripped));
                Editor.Spans = editorSpans;
            }
            finally { _isSyncingFromProperty = false; }
        }

        var combined = BuildCombinedSpans(editorSpans);
        if (SpansEquivalent(combined, Spans))
            return;

        _isHandlingEditorTextChanged = true;
        try
        {
            _isSyncingFromProperty = true;
            Spans = combined;
        }
        finally
        {
            _isSyncingFromProperty = false;
            _isHandlingEditorTextChanged = false;
        }

        UpdatePreviewMarkdown();
        SyncImageViewItems();
        SpansChanged?.Invoke(Spans);
        CommitHistoryIfChanged("Edit text");
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsPreviewMode)
            return;
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 || (e.KeyModifiers & KeyModifiers.Alt) != 0)
            return;

        if (e.Key == Key.C)
        {
            _ = CopySelectionAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.X)
        {
            if (IsReadOnly)
                return;
            _ = CutSelectionAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V)
        {
            if (IsReadOnly)
                return;
            _ = PasteSelectionAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _ = RedoAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z)
        {
            _ = UndoAsync();
            e.Handled = true;
            return;
        }

        if (IsReadOnly)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.S)
        {
            ApplyInlineFormat(InlineFormatKind.Strikethrough);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.H)
        {
            ApplyInlineFormat(InlineFormatKind.Highlight, ResolveHighlightColor());
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.B:
                ApplyInlineFormat(InlineFormatKind.Bold);
                e.Handled = true;
                break;
            case Key.I:
                ApplyInlineFormat(InlineFormatKind.Italic);
                e.Handled = true;
                break;
            case Key.U:
                ApplyInlineFormat(InlineFormatKind.Underline);
                e.Handled = true;
                break;
            case Key.L when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _ = EditLinkAsync();
                e.Handled = true;
                break;
        }
    }

    // ── Toolbar button handlers ──────────────────────────────────────────────

    private void OnBoldClick(object? sender, RoutedEventArgs e) => ApplyInlineFormat(InlineFormatKind.Bold);

    private void OnItalicClick(object? sender, RoutedEventArgs e) => ApplyInlineFormat(InlineFormatKind.Italic);

    private void OnUnderlineClick(object? sender, RoutedEventArgs e) => ApplyInlineFormat(InlineFormatKind.Underline);

    private void OnStrikethroughClick(object? sender, RoutedEventArgs e) => ApplyInlineFormat(InlineFormatKind.Strikethrough);

    private void OnHighlightClick(object? sender, RoutedEventArgs e) => ApplyInlineFormat(InlineFormatKind.Highlight, ResolveHighlightColor());

    private void OnWriteModeClick(object? sender, RoutedEventArgs e) => IsPreviewMode = false;

    private void OnPreviewModeClick(object? sender, RoutedEventArgs e) => IsPreviewMode = true;

    private async void OnLinkClick(object? sender, RoutedEventArgs e) => await EditLinkAsync();

    private async void OnImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsToolbarEnabled)
                return;
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null)
                return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = T("InsertImage", "Flashcards"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.svg" }
                    }
                }
            }).ConfigureAwait(true);
            var first = files.FirstOrDefault();
            if (first == null)
                return;
            var path = first.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                return;
            if (!CanInsertMoreImages())
                return;

            var storedPath = path;
            if (_imageAssetService != null)
            {
                var imageId = Guid.NewGuid().ToString("n");
                var import = await _imageAssetService.ImportAndCopyAsync(path, imageId).ConfigureAwait(true);
                if (import.IsSuccess && !string.IsNullOrWhiteSpace(import.Value))
                    storedPath = import.Value!;
            }

            var defaultAlign = HasBodyText() ? "right" : "center";
            _imageEntries.Add((storedPath, defaultAlign));
            _currentImageIndex = _imageEntries.Count - 1;
            CommitImageChange();
        }
        catch (Exception ex)
        {
            // Guard async void handler from tearing down the UI thread on unexpected import/runtime failures.
            if (_overlayService != null)
            {
                await _overlayService.CreateDialogAsync(
                    T("InsertImage", "Flashcards"),
                    ex.Message,
                    T("OK", "Common"),
                    string.Empty).ConfigureAwait(true);
            }
        }
    }

    // ── Image panel handlers ─────────────────────────────────────────────────

    private void OnAlignLeftClick(object? sender, RoutedEventArgs e) => ApplyCurrentImageAlign("left");

    private void OnAlignCenterClick(object? sender, RoutedEventArgs e) => ApplyCurrentImageAlign("center");

    private void OnAlignRightClick(object? sender, RoutedEventArgs e) => ApplyCurrentImageAlign("right");

    private void OnDeleteImageClick(object? sender, RoutedEventArgs e)
    {
        if (_currentImageIndex < 0 || _currentImageIndex >= _imageEntries.Count)
            return;
        _imageEntries.RemoveAt(_currentImageIndex);
        _currentImageIndex = Math.Max(0, Math.Min(_currentImageIndex, _imageEntries.Count - 1));
        CommitImageChange();
    }

    private void OnPreviousImageClick(object? sender, RoutedEventArgs e)
    {
        if (_currentImageIndex <= 0)
            return;
        _currentImageIndex--;
        RaiseCarouselProperties();
    }

    private void OnNextImageClick(object? sender, RoutedEventArgs e)
    {
        if (_currentImageIndex >= _embeddedImages.Count - 1)
            return;
        _currentImageIndex++;
        RaiseCarouselProperties();
    }

    private void RaiseCarouselProperties()
    {
        RaisePropertyChanged(nameof(CurrentImage));
        RaisePropertyChanged(nameof(CurrentImagePath));
        RaisePropertyChanged(nameof(IsCurrentImageAlignLeft));
        RaisePropertyChanged(nameof(IsCurrentImageAlignCenter));
        RaisePropertyChanged(nameof(IsCurrentImageAlignRight));
        RaisePropertyChanged(nameof(CanGoToPreviousImage));
        RaisePropertyChanged(nameof(CanGoToNextImage));
        RaisePropertyChanged(nameof(ImageCountLabel));
    }

    private void ApplyCurrentImageAlign(string align)
    {
        if (_currentImageIndex < 0 || _currentImageIndex >= _imageEntries.Count)
            return;
        var (path, _) = _imageEntries[_currentImageIndex];
        _imageEntries[_currentImageIndex] = (path, NormalizeAlign(align));
        CommitImageChange();
    }

    // ── Image layer helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="spans"/> for embedded image tokens and populates
    /// <see cref="_imageEntries"/>.  Resets <see cref="_currentImageIndex"/> to 0.
    /// </summary>
    private void ExtractImageEntries(IReadOnlyList<InlineSpan> spans)
    {
        _imageEntries.Clear();
        var flat = InlineSpanFormatApplier.Flatten(spans);
        foreach (Match m in EmbeddedImageRegex.Matches(flat))
        {
            var path = m.Groups["path"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(path))
                _imageEntries.Add((path, NormalizeAlign(m.Groups["align"].Value)));
        }
        _currentImageIndex = 0;
    }

    /// <summary>
    /// Returns a copy of <paramref name="spans"/> with all embedded image tokens
    /// stripped so the inner <see cref="RichTextEditor"/> only shows text.
    /// </summary>
    private static IReadOnlyList<InlineSpan> BuildTextOnlySpans(IReadOnlyList<InlineSpan> spans)
    {
        var flat = InlineSpanFormatApplier.Flatten(spans);
        if (!EmbeddedImageRegex.IsMatch(flat))
            return spans;

        var stripped = StripImageTokensFromText(flat);
        if (string.Equals(stripped, flat, StringComparison.Ordinal))
            return spans;

        return InlineSpanFormatApplier.Normalize(
            InlineSpanFormatApplier.ApplyTextEdit(spans, flat, stripped));
    }

    /// <summary>
    /// Merges <paramref name="textOnlySpans"/> with the current
    /// <see cref="_imageEntries"/> to produce the canonical combined payload
    /// stored in <see cref="Spans"/>.
    /// </summary>
    private IReadOnlyList<InlineSpan> BuildCombinedSpans(IReadOnlyList<InlineSpan> textOnlySpans)
    {
        if (_imageEntries.Count == 0)
            return InlineSpanFormatApplier.Normalize(new List<InlineSpan>(textOnlySpans));

        var imageTokenText = new StringBuilder();
        foreach (var (path, align) in _imageEntries)
            imageTokenText.Append($"![image]({path}){{align={align}}}");

        var combined = new List<InlineSpan>(textOnlySpans) { InlineSpan.Plain(imageTokenText.ToString()) };
        return InlineSpanFormatApplier.Normalize(combined);
    }

    /// <summary>
    /// Rebuilds <see cref="Spans"/> from the current editor text +
    /// <see cref="_imageEntries"/>, then fires <see cref="SpansChanged"/>.
    /// </summary>
    private void CommitImageChange()
    {
        var textOnly = Editor.Spans ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) };
        var combined = BuildCombinedSpans(textOnly);
        _isSyncingFromProperty = true;
        try { Spans = combined; }
        finally { _isSyncingFromProperty = false; }

        UpdatePreviewMarkdown();
        SyncImageViewItems();
        RefreshCanInsertImage();
        SpansChanged?.Invoke(Spans);
        CommitHistoryIfChanged("Edit image");
    }

    private static string StripImageTokensFromText(string text) =>
        // Preserve user-entered whitespace exactly; only strip image tokens.
        EmbeddedImageRegex.Replace(text, string.Empty);

    private bool HasBodyText()
    {
        var flat = InlineSpanFormatApplier.Flatten(
            Editor.Spans ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) });
        return !string.IsNullOrWhiteSpace(flat);
    }

    private void SyncImageViewItems()
    {
        _embeddedImages.Clear();
        for (var i = 0; i < _imageEntries.Count; i++)
        {
            var (path, align) = _imageEntries[i];
            _embeddedImages.Add(new RichDocumentEmbeddedImageViewItem
            {
                Index = i,
                Path = path,
                Align = align
            });
        }

        if (_embeddedImages.Count > 0 && _currentImageIndex >= _embeddedImages.Count)
            _currentImageIndex = _embeddedImages.Count - 1;

        RaisePropertyChanged(nameof(EmbeddedImages));
        RaisePropertyChanged(nameof(HasEmbeddedImages));
        RaisePropertyChanged(nameof(CurrentImage));
        RaisePropertyChanged(nameof(HasImages));
        RaisePropertyChanged(nameof(HasMultipleImages));
        RaiseCarouselProperties();
    }

    private bool CanInsertMoreImages()
    {
        var maxImages = HasBodyText() ? 1 : 2;
        return _imageEntries.Count < maxImages;
    }

    private void RefreshCanInsertImage()
    {
        var next = IsToolbarEnabled && CanInsertMoreImages();
        if (next == CanInsertImage)
            return;
        CanInsertImage = next;
        RaisePropertyChanged(nameof(CanInsertImage));
    }

    private static string NormalizeAlign(string? align) =>
        align?.Trim().ToLowerInvariant() switch
        {
            "left" => "left",
            "right" => "right",
            _ => "center"
        };

    // ── Link editing ─────────────────────────────────────────────────────────

    private async Task EditLinkAsync()
    {
        if (!IsToolbarEnabled || _overlayService == null)
            return;
        var range = GetSelectionRange();
        if (range == null)
        {
            var word = Editor.TryGetWordRangeAtCaret();
            if (word == null)
                return;
            Editor.SelectionStart = word.Value.Start;
            Editor.SelectionEnd = word.Value.End;
            Editor.CaretIndex = word.Value.End;
            range = (word.Value.Start, word.Value.End);
        }

        if (range == null)
            return;
        var (start, end) = range.Value;

        // Use Editor.Spans (text-only) so caret positions align correctly.
        var editorSpans = Editor.Spans ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) };
        var flat = InlineSpanFormatApplier.Flatten(editorSpans);
        if (end > flat.Length || end <= start)
            return;

        var selectedText = flat[start..end];
        var initialUrl = GetLinkUrlForRange(editorSpans, start, end) ?? string.Empty;
        var result = await ShowLinkDialogAsync(initialUrl, selectedText).ConfigureAwait(true);
        if (result == null)
            return;

        if (result.RemoveLinkRequested)
        {
            ApplyInlineFormatToRange(start, end, InlineFormatKind.Link, null);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Url))
            return;

        var normalized = NormalizeUrl(result.Url);
        var replacedDisplay = string.IsNullOrWhiteSpace(result.DisplayText) ? selectedText : result.DisplayText.Trim();
        var currentFlat = InlineSpanFormatApplier.Flatten(editorSpans);
        if (start > currentFlat.Length)
            return;
        var replaceEnd = Math.Clamp(end, start, currentFlat.Length);
        var newFlat = currentFlat[..start] + replacedDisplay + currentFlat[replaceEnd..];
        var runs = InlineSpanFormatApplier.ApplyTextEdit(editorSpans, currentFlat, newFlat);
        var linkEnd = start + replacedDisplay.Length;
        runs = InlineSpanFormatApplier.Apply(runs, start, linkEnd, InlineFormatKind.Link, normalized);
        CommitRuns(runs, linkEnd);
    }

    private Task<LinkEditDialogResult?> ShowLinkDialogAsync(string url, string displayText)
    {
        if (_overlayService == null)
            return Task.FromResult<LinkEditDialogResult?>(null);

        var tcs = new TaskCompletionSource<LinkEditDialogResult?>();
        var dialog = new LinkInsertDialogOverlay
        {
            Title = T("InsertLinkTitle", "NotesEditor"),
            UrlLabel = T("InsertLinkUrlLabel", "NotesEditor"),
            Url = url,
            UrlPlaceholder = T("InsertLinkUrlPlaceholder", "NotesEditor"),
            DisplayLabel = T("InsertLinkDisplayLabel", "NotesEditor"),
            DisplayText = displayText,
            DisplayPlaceholder = T("InsertLinkDisplayPlaceholder", "NotesEditor"),
            ShowDisplaySection = true,
            ShowCrossBlockHint = false,
            ShowRemoveLink = !string.IsNullOrWhiteSpace(url),
            RemoveLinkText = T("InsertLinkRemoveLink", "NotesEditor"),
            ConfirmText = T("OK", "Common"),
            CancelText = T("Cancel", "Common"),
            RequireUrlForConfirm = false
        };

        var overlayId = _overlayService.CreateOverlay(dialog, new OverlayOptions
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = true,
            CloseOnEscape = true
        }, "RichDocumentLinkEdit");

        dialog.OnResult = result =>
        {
            _overlayService.CloseOverlay(overlayId);
            tcs.TrySetResult(result);
        };

        return tcs.Task;
    }

    private static string? GetLinkUrlForRange(IReadOnlyList<InlineSpan> spans, int start, int end)
    {
        int offset = 0;
        foreach (var span in spans)
        {
            var runLength = span is TextSpan t ? t.Text.Length : 1;
            var runEnd = offset + runLength;
            if (runEnd > start && offset < end)
            {
                var url = span switch
                {
                    TextSpan textSpan => textSpan.Style.LinkUrl,
                    EquationSpan equationSpan => equationSpan.Style.LinkUrl,
                    FractionSpan fractionSpan => fractionSpan.Style.LinkUrl,
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }

            offset = runEnd;
        }

        return null;
    }

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs))
            return abs.ToString();
        if (trimmed.Contains('@', StringComparison.Ordinal) && !trimmed.Contains("://", StringComparison.Ordinal))
            return "mailto:" + trimmed;
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            return "https://" + trimmed;
        return trimmed;
    }

    // ── Inline formatting ────────────────────────────────────────────────────

    private void ApplyInlineFormat(InlineFormatKind kind, string? color = null)
    {
        if (!IsToolbarEnabled)
            return;
        var range = GetSelectionRange();
        if (range == null)
        {
            var word = Editor.TryGetWordRangeAtCaret();
            if (word == null)
                return;
            range = (word.Value.Start, word.Value.End);
        }

        ApplyInlineFormatToRange(range.Value.Start, range.Value.End, kind, color);
    }

    private void ApplyInlineFormatToRange(int start, int end, InlineFormatKind kind, string? color)
    {
        // Always operate on the text-only editor spans so caret positions stay valid.
        var editorSpans = Editor.Spans ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) };
        var runs = InlineSpanFormatApplier.Apply(editorSpans, start, end, kind, color);
        CommitRuns(runs, end);
    }

    private (int Start, int End)? GetSelectionRange()
    {
        var start = Math.Min(Editor.SelectionStart, Editor.SelectionEnd);
        var end = Math.Max(Editor.SelectionStart, Editor.SelectionEnd);
        if (start >= end)
            return null;
        return (start, end);
    }

    /// <summary>
    /// Commits <paramref name="textOnlyRuns"/> (text-only spans after a format
    /// operation) to both <see cref="Editor"/> and <see cref="Spans"/> (combined).
    /// </summary>
    private void CommitRuns(IReadOnlyList<InlineSpan> textOnlyRuns, int caret)
    {
        var textOnly = InlineSpanFormatApplier.Normalize(textOnlyRuns);
        var combined = BuildCombinedSpans(textOnly);
        _isSyncingFromProperty = true;
        try
        {
            Spans = combined;
            Editor.Spans = textOnly;
            var clamped = Math.Clamp(caret, 0, Editor.TextLength);
            Editor.CaretIndex = clamped;
            Editor.SelectionStart = clamped;
            Editor.SelectionEnd = clamped;
            UpdatePreviewMarkdown();
            SyncImageViewItems();
            SpansChanged?.Invoke(Spans);
            CommitHistoryIfChanged("Format text");
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    private void UpdatePreviewMarkdown()
    {
        PreviewMarkdown = InlineMarkdownSerializer.SerializeSpans(Spans);
        RaisePropertyChanged(nameof(PreviewMarkdown));
    }

    // ── Mode toggle ──────────────────────────────────────────────────────────

    private void UpdateModeButtonClasses()
    {
        if (WriteModeButton == null || PreviewModeButton == null || Editor == null || PreviewHost == null)
            return;
        SetSelectedClass(WriteModeButton, !IsPreviewMode);
        SetSelectedClass(PreviewModeButton, IsPreviewMode);
        Editor.IsVisible = !IsPreviewMode;
        Editor.IsHitTestVisible = !IsPreviewMode;
        PreviewHost.IsVisible = IsPreviewMode;
        PreviewHost.IsHitTestVisible = IsPreviewMode;
    }

    private static void SetSelectedClass(Button button, bool selected)
    {
        var has = button.Classes.Contains("selected");
        if (selected && !has)
            button.Classes.Add("selected");
        else if (!selected && has)
            button.Classes.Remove("selected");
    }

    // ── Misc helpers ─────────────────────────────────────────────────────────

    private string? ResolveHighlightColor()
    {
        if (Application.Current?.TryFindResource("InlineHighlightColor", out var resource) == true
            && resource is Avalonia.Media.Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        return null;
    }

    private string T(string key, string ns) => _localization?.T(key, ns) ?? key;

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static bool SpansEquivalent(IReadOnlyList<InlineSpan>? left, IReadOnlyList<InlineSpan>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var l = left[i];
            var r = right[i];
            if (l.GetType() != r.GetType())
                return false;

            switch (l)
            {
                case TextSpan lt when r is TextSpan rt:
                    if (!string.Equals(lt.Text, rt.Text, StringComparison.Ordinal)
                        || !Equals(lt.Style, rt.Style))
                        return false;
                    break;
                case EquationSpan le when r is EquationSpan re:
                    if (!string.Equals(le.Latex, re.Latex, StringComparison.Ordinal)
                        || !Equals(le.Style, re.Style))
                        return false;
                    break;
                case FractionSpan lf when r is FractionSpan rf:
                    if (lf.Numerator != rf.Numerator
                        || lf.Denominator != rf.Denominator
                        || !Equals(lf.Style, rf.Style))
                        return false;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private EditorSnapshot CaptureSnapshot()
    {
        var normalized = InlineSpanFormatApplier.Normalize(
            Spans ?? new List<InlineSpan> { InlineSpan.Plain(string.Empty) });
        return new EditorSnapshot(
            normalized,
            Editor?.CaretIndex ?? 0,
            Editor?.SelectionStart ?? 0,
            Editor?.SelectionEnd ?? 0);
    }

    private void ApplySnapshot(EditorSnapshot snapshot)
    {
        _isApplyingHistory = true;
        try
        {
            LoadSpans(snapshot.Spans);
            var caret = Math.Clamp(snapshot.CaretIndex, 0, Editor.TextLength);
            var start = Math.Clamp(snapshot.SelectionStart, 0, Editor.SelectionIndexUpperBound);
            var end = Math.Clamp(snapshot.SelectionEnd, 0, Editor.SelectionIndexUpperBound);
            Editor.CaretIndex = caret;
            Editor.SelectionStart = start;
            Editor.SelectionEnd = end;
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    private void CommitHistoryIfChanged(string description)
    {
        if (_isApplyingHistory || History == null)
        {
            _historyBaseline = CaptureSnapshot();
            return;
        }

        var current = CaptureSnapshot();
        var before = _historyBaseline ?? current;
        if (!SnapshotsEquivalent(before, current))
        {
            History.Push(new RichDocumentEditOperation(
                description,
                before,
                current,
                ApplySnapshot));
        }
        _historyBaseline = current;
    }

    private static bool SnapshotsEquivalent(EditorSnapshot left, EditorSnapshot right) =>
        left.CaretIndex == right.CaretIndex
        && left.SelectionStart == right.SelectionStart
        && left.SelectionEnd == right.SelectionEnd
        && SpansEquivalent(left.Spans, right.Spans);

    private async Task CopySelectionAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        var start = Math.Min(Editor.SelectionStart, Editor.SelectionEnd);
        var end = Math.Max(Editor.SelectionStart, Editor.SelectionEnd);
        if (start >= end || end > Editor.Text.Length)
            return;

        await clipboard.SetTextAsync(Editor.Text[start..end]).ConfigureAwait(true);
    }

    private async Task CutSelectionAsync()
    {
        if (IsReadOnly)
            return;

        var start = Math.Min(Editor.SelectionStart, Editor.SelectionEnd);
        var end = Math.Max(Editor.SelectionStart, Editor.SelectionEnd);
        if (start >= end || end > Editor.Text.Length)
            return;

        await CopySelectionAsync().ConfigureAwait(true);
        Editor.InsertTextAtCaret(string.Empty);
    }

    private async Task PasteSelectionAsync()
    {
        if (IsReadOnly)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        var text = await clipboard.GetTextAsync().ConfigureAwait(true);
        if (string.IsNullOrEmpty(text))
            return;

        Editor.InsertTextAtCaret(text);
    }

    private async Task UndoAsync()
    {
        if (History == null || !History.CanUndo)
            return;

        await History.UndoAsync().ConfigureAwait(true);
        _historyBaseline = CaptureSnapshot();
    }

    private async Task RedoAsync()
    {
        if (History == null || !History.CanRedo)
            return;

        await History.RedoAsync().ConfigureAwait(true);
        _historyBaseline = CaptureSnapshot();
    }

    private sealed record EditorSnapshot(
        IReadOnlyList<InlineSpan> Spans,
        int CaretIndex,
        int SelectionStart,
        int SelectionEnd);

    private sealed class RichDocumentEditOperation : IHistoryOperation
    {
        private readonly EditorSnapshot _before;
        private readonly EditorSnapshot _after;
        private readonly Action<EditorSnapshot> _apply;

        public RichDocumentEditOperation(
            string description,
            EditorSnapshot before,
            EditorSnapshot after,
            Action<EditorSnapshot> apply)
        {
            Description = description;
            _before = before;
            _after = after;
            _apply = apply;
        }

        public string Description { get; }
        public OperationSource Source => OperationSource.NotesEditor;

        public Task ApplyAsync()
        {
            _apply(_after);
            return Task.CompletedTask;
        }

        public Task RollbackAsync()
        {
            _apply(_before);
            return Task.CompletedTask;
        }
    }
}
