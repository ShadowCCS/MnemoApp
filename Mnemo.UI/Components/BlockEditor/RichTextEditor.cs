using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia.Threading;
using Avalonia.Layout;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives.PopupPositioning;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.LaTeX;
using Mnemo.Infrastructure.Services.TextShortcuts;
using Mnemo.UI.Controls;
using Mnemo.UI;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>
/// Custom editable control that renders styled <see cref="InlineSpan"/> lists directly via
/// <see cref="TextLayout"/>, giving pixel-accurate caret and selection geometry that stays
/// aligned with the visible rich text at all times.
/// </summary>
public class RichTextEditor : Control, ICustomHitTest
{
    // ── Avalonia properties ──────────────────────────────────────────────────

    public static readonly StyledProperty<IReadOnlyList<InlineSpan>> SpansProperty =
        AvaloniaProperty.Register<RichTextEditor, IReadOnlyList<InlineSpan>>(
            nameof(Spans), defaultValue: Array.Empty<InlineSpan>());

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<RichTextEditor, string?>(nameof(Watermark));

    /// <summary>
    /// When true, empty text + non-null <see cref="Watermark"/> is drawn without requiring pointer-over or focus on this control
    /// (e.g. image captions: parent sets Watermark while the pointer is over the image).
    /// </summary>
    public static readonly StyledProperty<bool> ShowInactiveWatermarkProperty =
        AvaloniaProperty.Register<RichTextEditor, bool>(nameof(ShowInactiveWatermark), defaultValue: false);

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<RichTextEditor, double>(nameof(FontSize), defaultValue: 16.0);

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<RichTextEditor, FontWeight>(nameof(FontWeight), defaultValue: FontWeight.Normal);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<RichTextEditor, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> CaretBrushProperty =
        AvaloniaProperty.Register<RichTextEditor, IBrush?>(nameof(CaretBrush));

    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<RichTextEditor, IBrush?>(nameof(SelectionBrush));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<RichTextEditor, bool>(nameof(IsReadOnly), defaultValue: false);

    /// <summary>
    /// When true, inline equations show their raw LaTeX source while hovered.
    /// </summary>
    public static readonly StyledProperty<bool> ShowInlineEquationSourceOnHoverProperty =
        AvaloniaProperty.Register<RichTextEditor, bool>(nameof(ShowInlineEquationSourceOnHover), defaultValue: false);

    public static readonly DirectProperty<RichTextEditor, int> CaretIndexProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, int>(
            nameof(CaretIndex), o => o.CaretIndex, (o, v) => o.CaretIndex = v);

    public static readonly DirectProperty<RichTextEditor, int> SelectionStartProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, int>(
            nameof(SelectionStart), o => o.SelectionStart, (o, v) => o.SelectionStart = v);

    public static readonly DirectProperty<RichTextEditor, int> SelectionEndProperty =
        AvaloniaProperty.RegisterDirect<RichTextEditor, int>(
            nameof(SelectionEnd), o => o.SelectionEnd, (o, v) => o.SelectionEnd = v);

    // ── Backing fields ───────────────────────────────────────────────────────

    private int _caretIndex;
    private int _selectionStart;
    private int _selectionEnd;
    private TextLayout? _textLayout;
    private TextLayout? _backgroundLayout;
    private TextLayout? _watermarkLayout;
    /// <summary>Text we built the current _textLayout for; used to detect stale layout when Spans were set after first paint.</summary>
    private string? _lastBuiltText;
    /// <summary>Logical caret boundary <c>k</c> → layout character index (same convention as <see cref="TextLayout"/>).</summary>
    private int[]? _layoutBoundaryAtLogical;
    /// <summary>Length of the expanded layout string (spaces reserve inline math width).</summary>
    private int _layoutTextLength;
    /// <summary>Width we last built layout for; used in Render to avoid building with Bounds (can be 0 or stale) and to prevent layout loops.</summary>
    private double _lastLayoutWidth;
    private bool _caretVisible = true;
    private DispatcherTimer? _caretTimer;
    private bool _isDragging;
    private int _dragAnchor;
    private AutoShortcutUndoState? _pendingAutoShortcutUndo;

    /// <summary>
    /// When set, the sub/sup flags of this style are forced onto every newly inserted character run,
    /// overriding style inheritance from adjacent spans. Used for sticky sub/sup typing mode.
    /// Set <c>null</c> to resume natural style inheritance.
    /// Set <c>ClearPendingInsertStyleAfterNextInsert = true</c> to auto-clear after one insertion.
    /// </summary>
    private Core.Models.TextStyle? _pendingInsertStyle;
    private bool _clearPendingInsertStyleAfterNextInsert;

    private static readonly FontFamily MonoFont =
        new("Cascadia Code, Consolas, Courier New, monospace");
    private static readonly Regex TailFractionTokenRegex = new(@"\\\d+/\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Cached inline equation layouts keyed by (charIndex, latex). Rebuilt when runs change.</summary>
    private readonly List<InlineEquationEntry> _inlineEquations = new();
    private readonly LRUCache<(string, double, uint), FormattedText> _mathTextCache = new(200);
    private ILaTeXEngine? _latexEngine;
    private bool _inlineEquationsDirty = true;

    /// <summary>Extra space so inline math ink (fractions, integrals) is not clipped vs text line metrics.</summary>
    private double _mathPadLeft, _mathPadTop, _mathPadRight, _mathPadBottom;

    /// <summary>While the inline equation flyout is open, LaTeX is previewed from here without committing <see cref="SpansProperty"/>.</summary>
    private bool _inlineEqPreviewActive;
    private string? _inlineEqPreviewLatex;
    private DispatcherTimer? _inlineEqRebuildTimer;
    /// <summary>Escape sets this so <see cref="Closed"/> does not commit to the document.</summary>
    private bool _inlineEqSuppressCloseCommit;
    private int? _hoveredInlineEquationCharIndex;
    private readonly object _spellcheckSync = new();
    private readonly List<SpellcheckIssue> _spellcheckIssues = [];
    private DispatcherTimer? _spellcheckDebounceTimer;
    private CancellationTokenSource? _spellcheckCts;
    private bool _spellcheckEnabled = true;
    private List<string> _spellcheckLanguages = ["en"];
    private bool _spellcheckInitialized;
    private string _lastSpellcheckText = string.Empty;

    private static readonly string[] SpellcheckSettingKeys =
    [
        "Editor.SpellCheck",
        "Editor.SpellCheckLanguages"
    ];

    private static ISpellcheckService? _spellcheckService;
    private static ISettingsService? _settingsService;

    private sealed class InlineEquationEntry
    {
        public int CharIndex;
        public string Latex = string.Empty;
        public Box? Layout;
        public double Width;
        public double Height;
    }

    private sealed class AutoShortcutUndoState
    {
        public int ReplacementStart;
        public string Replacement = string.Empty;
        public string OriginalSequence = string.Empty;
        public int TextLengthAfterConversion;
    }

    // ── Routed events ────────────────────────────────────────────────────────

    public static readonly RoutedEvent<TextChangedEventArgs> TextChangedEvent =
        RoutedEvent.Register<RichTextEditor, TextChangedEventArgs>(
            nameof(TextChanged), RoutingStrategies.Bubble);

    public event EventHandler<TextChangedEventArgs>? TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    /// <summary>
    /// When set, Ctrl+click on a link invokes this instead of <see cref="OpenExternalUrl"/>.
    /// Use for confirmation before leaving the app (e.g. browser / mail client).
    /// </summary>
    public Func<string, Task>? ExternalLinkNavigationRequested { get; set; }

    // ── Public API ───────────────────────────────────────────────────────────

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

    public bool ShowInactiveWatermark
    {
        get => GetValue(ShowInactiveWatermarkProperty);
        set => SetValue(ShowInactiveWatermarkProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? CaretBrush
    {
        get => GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public int CaretIndex
    {
        get => _caretIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, TextLength);
            if (SetAndRaise(CaretIndexProperty, ref _caretIndex, clamped))
                InvalidateVisual();
        }
    }

    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            var clamped = Math.Clamp(value, 0, SelectionIndexUpperBound);
            if (SetAndRaise(SelectionStartProperty, ref _selectionStart, clamped))
                InvalidateVisual();
        }
    }

    public int SelectionEnd
    {
        get => _selectionEnd;
        set
        {
            var clamped = Math.Clamp(value, 0, SelectionIndexUpperBound);
            if (SetAndRaise(SelectionEndProperty, ref _selectionEnd, clamped))
                InvalidateVisual();
        }
    }

    /// <summary>Flat text derived from the current runs.</summary>
    public string Text => FlattenRuns(Spans ?? Array.Empty<InlineSpan>());

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowInlineEquationSourceOnHover
    {
        get => GetValue(ShowInlineEquationSourceOnHoverProperty);
        set => SetValue(ShowInlineEquationSourceOnHoverProperty, value);
    }

    public int TextLength => Text.Length;

    /// <summary>
    /// Max selection index (half-open range). Empty text uses 1 so cross-block drag can highlight blank
    /// paragraphs; <see cref="CaretIndex"/> still clamps to <see cref="TextLength"/> (0).
    /// </summary>
    public int SelectionIndexUpperBound => TextLength == 0 ? 1 : TextLength;

    // ── Initialisation ───────────────────────────────────────────────────────

    static RichTextEditor()
    {
        FocusableProperty.OverrideDefaultValue<RichTextEditor>(true);
        CursorProperty.OverrideDefaultValue<RichTextEditor>(new Cursor(StandardCursorType.Ibeam));
        ClipToBoundsProperty.OverrideDefaultValue<RichTextEditor>(false);
        MinWidthProperty.OverrideDefaultValue<RichTextEditor>(2.0);
        SpansProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.OnSpansChanged());
        FontSizeProperty.Changed.AddClassHandler<RichTextEditor>((o, e) =>
        {
            o._inlineEquationsDirty = true;
            o.InvalidateLayout();
#pragma warning disable CS4014
            o.RebuildInlineEquationsAsync();
#pragma warning restore CS4014
        });
        FontWeightProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateLayout());
        ForegroundProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateLayout());
        WatermarkProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateLayout());
        ShowInactiveWatermarkProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateVisual());
        ShowInlineEquationSourceOnHoverProperty.Changed.AddClassHandler<RichTextEditor>((o, _) =>
        {
            o._hoveredInlineEquationCharIndex = null;
            o.InvalidateVisual();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartCaretTimer();
        _ = InitializeSpellcheckAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopCaretTimer();
        _inlineEqRebuildTimer?.Stop();
        _inlineEqRebuildTimer = null;
        _spellcheckDebounceTimer?.Stop();
        _spellcheckDebounceTimer = null;
        _spellcheckCts?.Cancel();
        _spellcheckCts?.Dispose();
        _spellcheckCts = null;
        if (_settingsService != null)
            _settingsService.SettingChanged -= OnSettingChanged;
        DisposeLayouts();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private const double MinLayoutWidth = 200;
    /// <summary>Max width when measure is unconstrained; avoids infinite desired size and layout loops.</summary>
    private const double MaxLayoutWidth = 4096;
    /// <summary>Font size ratio for subscript and superscript text relative to the base font size.</summary>
    private const double SubSuperscriptFontSizeRatio = 0.75;

    /// <summary>
    /// Width for <see cref="BuildLayout"/> / hit-test sync: <see cref="Bounds"/> can still be narrow before the
    /// next arrange even when the visual parent (e.g. column) is already wide — max with parent avoids stale layout.
    /// </summary>
    private double ComputeEffectiveLayoutMaxWidth()
    {
        var wSelf = Bounds.Width > 0 && !double.IsNaN(Bounds.Width) ? Bounds.Width : 0;
        var wArrange = _lastLayoutWidth > 0 && !double.IsNaN(_lastLayoutWidth) ? _lastLayoutWidth : 0;
        var wParent = 0.0;
        if (this.GetVisualParent() is Control pc && pc.Bounds.Width > 0 && !double.IsNaN(pc.Bounds.Width))
            wParent = pc.Bounds.Width;

        var m = Math.Max(wSelf, Math.Max(wArrange, wParent));
        if (m <= 0)
            return MinLayoutWidth;
        return Math.Min(MaxLayoutWidth, m);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Never use infinite width: causes huge desired size and can trigger infinite layout loop.
        var maxWidth = availableSize.Width > 0 && !double.IsInfinity(availableSize.Width)
            ? availableSize.Width : MaxLayoutWidth;
        BuildLayout(maxWidth);
        var textH = _textLayout?.Height ?? FontSize;
        var textW = _textLayout?.Width ?? MinLayoutWidth;
        var height = textH + _mathPadTop + _mathPadBottom;
        var intrinsicW = textW + _mathPadLeft + _mathPadRight;
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? Math.Max(MinLayoutWidth, Math.Min(MaxLayoutWidth, intrinsicW))
            : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layoutWidth = finalSize.Width > 0 ? finalSize.Width : MinLayoutWidth;
        _lastLayoutWidth = layoutWidth;
        BuildLayout(layoutWidth);
        return finalSize;
    }

    private void BuildLayout(double maxWidth)
    {
        if (maxWidth <= 0 || double.IsNaN(maxWidth))
            maxWidth = MinLayoutWidth;
        DisposeLayouts();

        var runs = Spans ?? Array.Empty<InlineSpan>();
        var text = FlattenRuns(runs);
        // Use an explicit opaque brush so text is always visible (theme/resolution can make DynamicResource brush wrong at measure time).
        var foreground = Foreground ?? GetThemeForeground();
        if (foreground?.Opacity == 0)
            foreground = new SolidColorBrush(Colors.Black);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight);

        if (string.IsNullOrEmpty(text))
        {
            // Empty — still build a zero-char layout so HitTest works
            _textLayout = new TextLayout(
                string.Empty, typeface, FontSize, foreground,
                TextAlignment.Left, TextWrapping.Wrap, TextTrimming.None,
                null, FlowDirection.LeftToRight, maxWidth);

            // Watermark
            var wmText = Watermark ?? string.Empty;
            if (!string.IsNullOrEmpty(wmText))
            {
                _watermarkLayout = new TextLayout(
                    wmText, typeface, FontSize,
                    new SolidColorBrush(Colors.Gray, 0.5),
                    TextAlignment.Left, TextWrapping.Wrap, TextTrimming.None,
                    null, FlowDirection.LeftToRight, maxWidth);
            }
            _lastBuiltText = string.Empty;
            _layoutBoundaryAtLogical = new int[] { 0 };
            _layoutTextLength = 0;
            _backgroundLayout = null;
            return;
        }

        // Ensure non-null opaque foreground so glyphs are always drawn.
        var safeForeground = foreground ?? new SolidColorBrush(Colors.Black);
        if (safeForeground.Opacity == 0)
            safeForeground = new SolidColorBrush(Colors.Black);

        var (layoutText, backgroundSpans, foregroundSpans, boundaries) = BuildExpandedLayoutText(runs, text.Length, safeForeground, typeface);
        _layoutBoundaryAtLogical = boundaries;
        _layoutTextLength = layoutText.Length;

        _textLayout = new TextLayout(
            layoutText, typeface, FontSize, safeForeground,
            TextAlignment.Left, TextWrapping.Wrap, TextTrimming.None,
            null, FlowDirection.LeftToRight, maxWidth,
            double.PositiveInfinity, double.NaN, 0, 0,
            foregroundSpans.Count > 0 ? foregroundSpans : null);

        _backgroundLayout = new TextLayout(
            layoutText, typeface, FontSize, Brushes.Transparent,
            TextAlignment.Left, TextWrapping.Wrap, TextTrimming.None,
            null, FlowDirection.LeftToRight, maxWidth,
            double.PositiveInfinity, double.NaN, 0, 0,
            backgroundSpans.Count > 0 ? backgroundSpans : null);
        _lastBuiltText = text;
    }

    /// <summary>Layout string uses spaces to reserve measured inline-math width (Notion-style flow).</summary>
    private (string LayoutText, List<ValueSpan<TextRunProperties>> BackgroundSpans, List<ValueSpan<TextRunProperties>> ForegroundSpans, int[] Boundaries) BuildExpandedLayoutText(
        IReadOnlyList<InlineSpan> docSpans,
        int logicalLen,
        IBrush defaultForeground,
        Typeface defaultTypeface)
    {
        var sb = new StringBuilder();
        var backgroundSpans = new List<ValueSpan<TextRunProperties>>(docSpans.Count * 2);
        var foregroundSpans = new List<ValueSpan<TextRunProperties>>(docSpans.Count * 2);
        var boundaries = new int[logicalLen + 1];
        boundaries[0] = 0;
        int logicalIdx = 0;
        int layoutOffset = 0;

        foreach (var seg in docSpans)
        {
            if (seg is TextSpan run)
            {
                if (run.Text.Length == 0) continue;

                var style = run.Style;
                var ff = style.Code ? MonoFont : FontFamily.Default;
                var fw = style.Bold ? FontWeight.Bold : FontWeight.Normal;
                var fs = style.Italic ? FontStyle.Italic : FontStyle.Normal;
                var runTypeface = new Typeface(ff, fs, fw);

                TextDecorationCollection? decorations = null;
                if (style.Underline || style.Strikethrough || !string.IsNullOrEmpty(style.LinkUrl))
                {
                    decorations = new TextDecorationCollection();
                    if (style.Underline || !string.IsNullOrEmpty(style.LinkUrl))
                        decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
                    if (style.Strikethrough)
                        decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
                }

                IBrush runForeground = defaultForeground;
                if (!string.IsNullOrEmpty(style.LinkUrl)
                    && Application.Current?.TryFindResource("LinksBrush", out var linkRes) == true
                    && linkRes is IBrush linkBrush)
                    runForeground = linkBrush;

                var background = ResolveInlineBackgroundBrush(style);

                double runFontSize = FontSize;
                var runBaseline = BaselineAlignment.Baseline;
                if (style.Subscript)
                {
                    runFontSize = FontSize * SubSuperscriptFontSizeRatio;
                    runBaseline = BaselineAlignment.Subscript;
                }
                else if (style.Superscript)
                {
                    runFontSize = FontSize * SubSuperscriptFontSizeRatio;
                    runBaseline = BaselineAlignment.Superscript;
                }

                var bgProps = new GenericTextRunProperties(
                    runTypeface,
                    fontRenderingEmSize: runFontSize,
                    textDecorations: null,
                    foregroundBrush: Brushes.Transparent,
                    backgroundBrush: background,
                    baselineAlignment: runBaseline);

                var fgProps = new GenericTextRunProperties(
                    runTypeface,
                    fontRenderingEmSize: runFontSize,
                    textDecorations: decorations,
                    foregroundBrush: runForeground,
                    backgroundBrush: null,
                    baselineAlignment: runBaseline);

                sb.Append(run.Text);
                backgroundSpans.Add(new ValueSpan<TextRunProperties>(layoutOffset, run.Text.Length, bgProps));
                foregroundSpans.Add(new ValueSpan<TextRunProperties>(layoutOffset, run.Text.Length, fgProps));
                for (var c = 0; c < run.Text.Length; c++)
                {
                    logicalIdx++;
                    layoutOffset++;
                    boundaries[logicalIdx] = layoutOffset;
                }
            }
            else if (seg is EquationSpan eq)
            {
                var style = eq.Style;
                var ff = style.Code ? MonoFont : FontFamily.Default;
                var fw = style.Bold ? FontWeight.Bold : FontWeight.Normal;
                var fs = style.Italic ? FontStyle.Italic : FontStyle.Normal;
                var runTypeface = new Typeface(ff, fs, fw);
                var background = ResolveInlineBackgroundBrush(style);
                var bgProps = new GenericTextRunProperties(
                    runTypeface,
                    fontRenderingEmSize: FontSize,
                    textDecorations: null,
                    foregroundBrush: Brushes.Transparent,
                    backgroundBrush: background);

                var fgProps = new GenericTextRunProperties(
                    runTypeface,
                    fontRenderingEmSize: FontSize,
                    textDecorations: null,
                    foregroundBrush: Brushes.Transparent,
                    backgroundBrush: null);

                var measureFg = ForegroundForTextMeasurement(defaultForeground);
                var placeholderAdv = MeasureRunTextWidth(InlineSpan.EquationAtomChar.ToString(), runTypeface, measureFg);
                double target = GetEquationTargetWidth(logicalIdx, placeholderAdv);
                int n = GetMinimalSpaceCountForTargetWidth(target, runTypeface, measureFg);
                for (var j = 0; j < n; j++)
                    sb.Append(' ');
                backgroundSpans.Add(new ValueSpan<TextRunProperties>(layoutOffset, n, bgProps));
                foregroundSpans.Add(new ValueSpan<TextRunProperties>(layoutOffset, n, fgProps));
                layoutOffset += n;
                logicalIdx++;
                boundaries[logicalIdx] = layoutOffset;
            }
            else if (seg is FractionSpan frac)
            {
                var style = frac.Style;
                var ff = style.Code ? MonoFont : FontFamily.Default;
                var fw = style.Bold ? FontWeight.Bold : FontWeight.Normal;
                var fs = style.Italic ? FontStyle.Italic : FontStyle.Normal;
                var runTypeface = new Typeface(ff, fs, fw);

                TextDecorationCollection? decorations = null;
                if (style.Underline || style.Strikethrough || !string.IsNullOrEmpty(style.LinkUrl))
                {
                    decorations = new TextDecorationCollection();
                    if (style.Underline || !string.IsNullOrEmpty(style.LinkUrl))
                        decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
                    if (style.Strikethrough)
                        decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
                }

                IBrush runForeground = defaultForeground;
                if (!string.IsNullOrEmpty(style.LinkUrl)
                    && Application.Current?.TryFindResource("LinksBrush", out var linkRes) == true
                    && linkRes is IBrush linkBrush)
                    runForeground = linkBrush;

                var background = ResolveInlineBackgroundBrush(style);

                var bgProps = new GenericTextRunProperties(
                    runTypeface,
                    fontRenderingEmSize: FontSize,
                    textDecorations: null,
                    foregroundBrush: Brushes.Transparent,
                    backgroundBrush: background);

                var fgProps = new GenericTextRunProperties(
                    runTypeface,
                    fontRenderingEmSize: FontSize,
                    textDecorations: decorations,
                    foregroundBrush: runForeground,
                    backgroundBrush: null);

                var rendered = FractionShortcutResolver.Render(frac.Numerator, frac.Denominator);
                sb.Append(rendered);
                backgroundSpans.Add(new ValueSpan<TextRunProperties>(layoutOffset, rendered.Length, bgProps));
                foregroundSpans.Add(new ValueSpan<TextRunProperties>(layoutOffset, rendered.Length, fgProps));
                layoutOffset += rendered.Length;
                logicalIdx++;
                boundaries[logicalIdx] = layoutOffset;
            }
        }

        return (sb.ToString(), backgroundSpans, foregroundSpans, boundaries);
    }

    private static double MeasureTextLayoutWidth(string text, Typeface typeface, IBrush fg, double fontSize)
    {
        if (text.Length == 0) return 0;
        using var tl = new TextLayout(
            text, typeface, fontSize, fg,
            TextAlignment.Left, TextWrapping.NoWrap, TextTrimming.None,
            null, FlowDirection.LeftToRight, 10000);
        return tl.Width > 0 ? tl.Width : fontSize * 0.25;
    }

    private double MeasureRunTextWidth(string text, Typeface typeface, IBrush fg) =>
        MeasureTextLayoutWidth(text, typeface, fg, FontSize);

    /// <summary>
    /// TextLayout measurement with transparent brushes can return widths far below real glyph advances, which
    /// inflates the space count for inline math and leaves a large empty band — measure with an opaque brush instead.
    /// </summary>
    private static IBrush ForegroundForTextMeasurement(IBrush defaultForeground)
    {
        if (defaultForeground is null || defaultForeground.Opacity <= 0)
            return Brushes.Black;
        return defaultForeground;
    }

    private double GetEquationTargetWidth(int logicalCharIndex, double placeholderAdvance)
    {
        var lineCap = _lastLayoutWidth > 0 ? _lastLayoutWidth : (Bounds.Width > 0 ? Bounds.Width : MinLayoutWidth);
        foreach (var eq in _inlineEquations)
        {
            if (eq.CharIndex == logicalCharIndex && eq.Width > 0)
                return ClampInlineEquationReserveWidth(eq.Width, eq.Latex ?? string.Empty, FontSize, lineCap);
        }

        return placeholderAdvance;
    }

    /// <summary>Minimal number of spaces so measured width ≥ target (same font as the equation run — avoids bold/code mismatch vs default space width).</summary>
    private int GetMinimalSpaceCountForTargetWidth(double targetWidth, Typeface typeface, IBrush fg)
    {
        if (targetWidth <= 0)
            return 1;
        double spaceW = MeasureRunTextWidth(" ", typeface, fg);
        if (spaceW <= 0)
            spaceW = FontSize * 0.25;
        // Guard: transparent or odd shaping can yield a tiny measured space while layout still advances ~0.25–0.5 em.
        spaceW = Math.Max(spaceW, FontSize * 0.22);
        int n = Math.Max(1, (int)Math.Ceiling(targetWidth / spaceW));
        while (n > 1)
        {
            double wPrev = MeasureRunTextWidth(new string(' ', n - 1), typeface, fg);
            if (wPrev >= targetWidth)
                n--;
            else
                break;
        }

        return n;
    }

    private int LogicalCaretToLayoutBoundary(int logicalCaret)
    {
        if (_layoutBoundaryAtLogical == null || _layoutBoundaryAtLogical.Length == 0)
            return 0;
        var maxLogical = _layoutBoundaryAtLogical.Length - 1;
        logicalCaret = Math.Clamp(logicalCaret, 0, maxLogical);
        return _layoutBoundaryAtLogical[logicalCaret];
    }

    private int LayoutBoundaryIndexToLogicalCaret(int layoutBoundaryIndex)
    {
        if (_layoutBoundaryAtLogical == null || _layoutBoundaryAtLogical.Length == 0)
            return 0;
        int maxLogical = _layoutBoundaryAtLogical.Length - 1;
        int maxBound = _layoutBoundaryAtLogical[maxLogical];
        layoutBoundaryIndex = Math.Clamp(layoutBoundaryIndex, 0, maxBound);

        // boundaries[k] = layout offset of logical caret k (monotone). Map layout probe -> caret index.
        int u = 0;
        while (u <= maxLogical && _layoutBoundaryAtLogical[u] <= layoutBoundaryIndex)
            u++;
        if (u > maxLogical)
            return maxLogical;

        int loCaret = u - 1;
        if (layoutBoundaryIndex == _layoutBoundaryAtLogical[loCaret])
            return loCaret;

        int hiBound = _layoutBoundaryAtLogical[u];
        if (layoutBoundaryIndex >= hiBound)
            return u;

        int loBound = _layoutBoundaryAtLogical[loCaret];
        if (hiBound <= loBound)
            return loCaret;

        int mid = (loBound + hiBound) / 2;
        return layoutBoundaryIndex < mid ? loCaret : loCaret + 1;
    }

    private void OnSpansChanged()
    {
        _inlineEquationsDirty = true;
        InvalidateLayout();
        var len = TextLength;
        if (_caretIndex > len) CaretIndex = len;
        int selMax = SelectionIndexUpperBound;
        if (_selectionStart > selMax) SelectionStart = selMax;
        if (_selectionEnd > selMax) SelectionEnd = selMax;
        RaiseEvent(new TextChangedEventArgs(TextChangedEvent));
        _ = RebuildInlineEquationsAsync();
        ScheduleSpellcheck();
        var runs = Spans ?? Array.Empty<InlineSpan>();
        if (runs.Any(static s => s is EquationSpan))
        {
            Dispatcher.UIThread.Post(() =>
            {
                _inlineEquationsDirty = true;
                _ = RebuildInlineEquationsAsync();
            }, DispatcherPriority.Loaded);
        }
    }

    private void InvalidateLayout()
    {
        DisposeLayouts();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void DisposeLayouts()
    {
        _backgroundLayout?.Dispose();
        _backgroundLayout = null;
        _textLayout?.Dispose();
        _textLayout = null;
        _watermarkLayout?.Dispose();
        _watermarkLayout = null;
        _lastBuiltText = null;
        _lastLayoutWidth = 0;
        _layoutBoundaryAtLogical = null;
        _layoutTextLength = 0;
    }

    private bool ShouldDrawWatermark() =>
        string.IsNullOrEmpty(Text)
        && _watermarkLayout != null
        && (IsFocused || IsPointerOver || ShowInactiveWatermark);

    private static IBrush GetThemeForeground()
    {
        if (Application.Current == null)
            return new SolidColorBrush(Colors.Gray);
        try
        {
            var brush = Application.Current.FindResource("TextPrimaryBrush");
            return brush is IBrush b ? b : new SolidColorBrush(Colors.Gray);
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    private static IBrush GetThemeSelectionBrush()
    {
        if (Application.Current?.TryFindResource("TextControlSelectionHighlightColorBrush", out var res) == true
            && res is IBrush brush)
            return brush;
        return new SolidColorBrush(Colors.CornflowerBlue, 0.4);
    }

    private static IBrush GetThemeCaretBrush()
    {
        if (Application.Current?.TryFindResource("TextControlForegroundBrush", out var fgRes) == true
            && fgRes is IBrush fgBrush)
            return fgBrush;
        if (Application.Current?.TryFindResource("TextPrimaryBrush", out var primaryRes) == true
            && primaryRes is IBrush primaryBrush)
            return primaryBrush;
        return Brushes.Black;
    }

    private static IBrush GetInlineHighlightBrush()
    {
        if (Application.Current?.TryFindResource("InlineHighlightColorBrush", out var brushRes) == true
            && brushRes is IBrush brush)
            return brush;
        if (Application.Current?.TryFindResource("InlineHighlightColor", out var colorRes) == true
            && colorRes is Color color)
            return new SolidColorBrush(color);
        return new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0xAA));
    }

    private static IBrush? ResolveInlineBackgroundBrush(TextStyle style)
    {
        if (style.Highlight)
            return GetInlineHighlightBrush();
        if (string.IsNullOrEmpty(style.BackgroundColor))
            return null;

        if (Color.TryParse(style.BackgroundColor, out var color))
            return new SolidColorBrush(color);

        if (style.BackgroundColor.StartsWith("swatch", StringComparison.OrdinalIgnoreCase)
            && Application.Current != null)
        {
            var key = "ColorSwatch" + style.BackgroundColor.Substring(6);
            if (Application.Current.TryFindResource(key, out var swatch))
            {
                if (swatch is Color swatchColor)
                    return new SolidColorBrush(swatchColor);
                if (swatch is IBrush swatchBrush)
                    return swatchBrush;
            }
        }

        return null;
    }

    private static string TNotes(string key)
    {
        var loc = (Application.Current as App)?.Services?.GetService<ILocalizationService>();
        return loc?.T(key, "NotesEditor") ?? key;
    }

    private static IBrush GetEquationActiveHighlightBrush()
    {
        if (Application.Current?.TryFindResource("OverlayHighlightColorBrush", out var r) == true && r is IBrush b)
            return b;
        return new SolidColorBrush(Color.FromArgb(0xCC, 0x46, 0x45, 0x49));
    }


    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        // Rebuild layout only if null or content changed. Use _lastLayoutWidth to avoid building with Bounds (can be 0 during first frame) which can cause layout loops.
        var currentText = Text;
        var layoutWidth = _lastLayoutWidth > 0 ? _lastLayoutWidth : (Bounds.Width > 0 ? Bounds.Width : MinLayoutWidth);
        if (_textLayout == null || currentText != _lastBuiltText)
            BuildLayout(layoutWidth);

        var origin = new Point(0, 0);

        using (context.PushTransform(Matrix.CreateTranslation(_mathPadLeft, _mathPadTop)))
        {
            int selStart = Math.Min(_selectionStart, _selectionEnd);
            int selEnd = Math.Max(_selectionStart, _selectionEnd);

            // 1. Run backgrounds only.
            if (_backgroundLayout != null)
                _backgroundLayout.Draw(context, origin);

            // 2. Selection overlay above run backgrounds.
            if (selEnd > selStart && _textLayout != null)
            {
                var selBrush = SelectionBrush ?? GetThemeSelectionBrush();
                if (string.IsNullOrEmpty(Text))
                {
                    double h = _textLayout.Height > 0 ? _textLayout.Height : FontSize;
                    double w = Math.Max(3.0, FontSize * 0.45);
                    context.FillRectangle(selBrush, new Rect(0, 0, w, h));
                }
                else
                {
                    int layoutSelStart = LogicalCaretToLayoutBoundary(selStart);
                    int layoutSelLen = LogicalCaretToLayoutBoundary(selEnd) - layoutSelStart;
                    // Avalonia HitTestTextRange requires non-zero length; mapping can be 0 on edge transitions.
                    List<Rect> rects;
                    if (layoutSelLen <= 0)
                        rects = new List<Rect>();
                    else
                        rects = _textLayout.HitTestTextRange(layoutSelStart, layoutSelLen).ToList();

                    bool hasDrawable = rects.Any(r => r.Width > 0.5 && r.Height > 0.5);
                    if (!hasDrawable)
                    {
                        // U+200B and other zero-advance glyphs often yield no range rects; draw a caret-sized chip.
                        try
                        {
                            int idx = TextLength > 0 ? LogicalCaretToLayoutBoundary(Math.Clamp(selStart, 0, TextLength - 1)) : 0;
                            var pos = _textLayout.HitTestTextPosition(idx);
                            double h = pos.Height > 0 ? pos.Height : (_textLayout.Height > 0 ? _textLayout.Height : FontSize);
                            double chipW = pos.Width > 0.5 ? pos.Width : Math.Max(3.0, FontSize * 0.45);
                            context.FillRectangle(selBrush, new Rect(pos.X, pos.Y, chipW, h));
                        }
                        catch
                        {
                            double h = _textLayout.Height > 0 ? _textLayout.Height : FontSize;
                            double chipW = Math.Max(3.0, FontSize * 0.45);
                            context.FillRectangle(selBrush, new Rect(0, 0, chipW, h));
                        }
                    }
                    else
                    {
                        foreach (var rect in rects)
                            context.FillRectangle(selBrush, rect.Translate(origin));
                    }
                }
            }

            // 3. Foreground glyphs and inline math.
            if (_textLayout != null)
            {
                _textLayout.Draw(context, origin);
                RenderSpellcheckUnderlines(context);
                RenderInlineEquations(context);
                if (ShouldDrawWatermark() && _watermarkLayout != null)
                    _watermarkLayout.Draw(context, origin);
            }
            // 4. Caret on top.
            if (IsFocused && _caretVisible && selEnd == selStart && _textLayout != null)
            {
                var caretBrush = CaretBrush ?? GetThemeCaretBrush();
                var caretRect = GetCaretRect();
                context.FillRectangle(caretBrush, caretRect);
            }

        }
    }

    /// <summary>Bounding rect of the first text line in local coordinates (Y/Height match the line box; width is at least one glyph wide).</summary>
    public Rect GetFirstLineBounds()
    {
        var layoutWidth = Bounds.Width > 0 ? Bounds.Width : (_lastLayoutWidth > 0 ? _lastLayoutWidth : MinLayoutWidth);
        if (layoutWidth <= 0 || double.IsNaN(layoutWidth))
            layoutWidth = MinLayoutWidth;
        var currentText = Text;
        if (_textLayout == null || currentText != _lastBuiltText)
            BuildLayout(layoutWidth);

        if (_textLayout != null && !string.IsNullOrEmpty(currentText))
        {
            try
            {
                var charRect = _textLayout.HitTestTextPosition(0);
                var h = charRect.Height > 0 ? charRect.Height : FontSize;
                var w = charRect.Width > 0 ? charRect.Width : 1;
                return new Rect(charRect.X + _mathPadLeft, charRect.Y + _mathPadTop, w, h);
            }
            catch
            {
                // fall through
            }
        }

        if (ShouldDrawWatermark() && _watermarkLayout != null)
        {
            try
            {
                var charRect = _watermarkLayout.HitTestTextPosition(0);
                var h = charRect.Height > 0 ? charRect.Height : FontSize;
                var w = charRect.Width > 0 ? charRect.Width : 1;
                return new Rect(charRect.X + _mathPadLeft, charRect.Y + _mathPadTop, w, h);
            }
            catch
            {
                // fall through
            }
        }

        return new Rect(_mathPadLeft, _mathPadTop, Bounds.Width > 0 ? Bounds.Width : layoutWidth, FontSize);
    }

    /// <summary>Returns the bounding rect of the current selection in local coordinates, or null if no selection or layout not ready.</summary>
    public Rect? GetSelectionBounds()
    {
        int selStart = Math.Min(_selectionStart, _selectionEnd);
        int selEnd = Math.Max(_selectionStart, _selectionEnd);
        if (selEnd <= selStart || _textLayout == null) return null;
        if (string.IsNullOrEmpty(Text))
        {
            double h = _textLayout.Height > 0 ? _textLayout.Height : FontSize;
            double w = Math.Max(3.0, FontSize * 0.45);
            return new Rect(_mathPadLeft, _mathPadTop, w, h);
        }
        try
        {
            int layoutSelStart = LogicalCaretToLayoutBoundary(selStart);
            int layoutSelLen = LogicalCaretToLayoutBoundary(selEnd) - layoutSelStart;
            if (layoutSelLen <= 0)
            {
                int fbIdx = TextLength > 0 ? LogicalCaretToLayoutBoundary(Math.Clamp(selStart, 0, TextLength - 1)) : 0;
                var fbPos = _textLayout.HitTestTextPosition(fbIdx);
                double fbH = fbPos.Height > 0 ? fbPos.Height : (_textLayout.Height > 0 ? _textLayout.Height : FontSize);
                double fbW = fbPos.Width > 0.5 ? fbPos.Width : Math.Max(3.0, FontSize * 0.45);
                return new Rect(fbPos.X + _mathPadLeft, fbPos.Y + _mathPadTop, fbW, fbH);
            }

            var rects = _textLayout.HitTestTextRange(layoutSelStart, layoutSelLen).ToList();
            bool hasDrawable = rects.Any(r => r.Width > 0.5 && r.Height > 0.5);
            if (hasDrawable)
            {
                var r = rects[0];
                for (int i = 1; i < rects.Count; i++)
                    r = r.Union(rects[i]);
                return r.Translate(new Vector(_mathPadLeft, _mathPadTop));
            }
            int idx = TextLength > 0 ? LogicalCaretToLayoutBoundary(Math.Clamp(selStart, 0, TextLength - 1)) : 0;
            var pos = _textLayout.HitTestTextPosition(idx);
            double h = pos.Height > 0 ? pos.Height : (_textLayout.Height > 0 ? _textLayout.Height : FontSize);
            double chipW = pos.Width > 0.5 ? pos.Width : Math.Max(3.0, FontSize * 0.45);
            return new Rect(pos.X + _mathPadLeft, pos.Y + _mathPadTop, chipW, h);
        }
        catch
        {
            return null;
        }
    }

    private Rect GetCaretRect()
    {
        if (_textLayout == null) return new Rect(0, 0, 1, FontSize);
        try
        {
            var layoutIdx = LogicalCaretToLayoutBoundary(_caretIndex);
            var charRect = _textLayout.HitTestTextPosition(layoutIdx);
            double x = charRect.X;
            double lineH = charRect.Height > 0 ? charRect.Height : FontSize;
            double lineY = charRect.Y;

            var (isSub, isSup) = GetCaretSubSupStyle();
            if (isSub || isSup)
            {
                var (subY, subH) = GetSubSupVerticalMetrics(lineY, lineH, isSub, isSup);
                return new Rect(x, subY, 1.5, subH);
            }
            return new Rect(x, lineY, 1.5, lineH);
        }
        catch
        {
            return new Rect(0, 0, 1.5, FontSize);
        }
    }

    /// <summary>
    /// Returns whether the caret should render as subscript or superscript.
    /// <para>
    /// When a pending insert style is active (sticky mode or escape mode) it takes full priority,
    /// so the caret immediately reflects the typing mode and correctly shows normal height even right
    /// after exiting sticky mode while the caret is still inside a sub/sup span.
    /// </para>
    /// <para>
    /// When no pending style is active (plain navigation), the style of the character immediately
    /// to the left of the caret is used, matching the visual expected from the next keystroke.
    /// </para>
    /// </summary>
    private (bool sub, bool sup) GetCaretSubSupStyle()
    {
        if (_pendingInsertStyle.HasValue)
            return (_pendingInsertStyle.Value.Subscript, _pendingInsertStyle.Value.Superscript);

        // Navigation: reflect the style of the character to the left of the caret.
        if (_caretIndex <= 0) return (false, false);
        var spans = Spans;
        int pos = 0;
        foreach (var span in spans)
        {
            int len = span is Core.Models.TextSpan ts ? ts.Text.Length : 1;
            int end = pos + len;
            if (pos < _caretIndex && end >= _caretIndex)
            {
                if (span is Core.Models.TextSpan textSpan)
                    return (textSpan.Style.Subscript, textSpan.Style.Superscript);
                return (false, false);
            }
            pos = end;
        }
        return (false, false);
    }

    /// <summary>
    /// Computes the Y offset and height for a sub/sup caret or bracket,
    /// preferring live glyph metrics from the text layout when an actual sub/sup character exists.
    /// </summary>
    private (double y, double height) GetSubSupVerticalMetrics(double lineY, double lineH, bool isSub, bool isSup)
    {
        double subH = FontSize * SubSuperscriptFontSizeRatio;

        // Try to sample actual glyph metrics from an existing sub/sup run in the layout.
        if (_textLayout != null)
        {
            var spans = Spans;
            int pos = 0;
            foreach (var span in spans)
            {
                if (span is Core.Models.TextSpan ts && ts.Text.Length > 0)
                {
                    bool match = (isSub && ts.Style.Subscript) || (isSup && ts.Style.Superscript);
                    if (match)
                    {
                        try
                        {
                            var idx = LogicalCaretToLayoutBoundary(pos);
                            var rect = _textLayout.HitTestTextPosition(idx);
                            if (rect.Height > 0 && rect.Height < lineH - 0.5)
                                return (rect.Y, rect.Height);
                        }
                        catch { }
                    }
                    pos += ts.Text.Length;
                }
                else pos++;
            }
        }

        // Fallback: approximate from line proportions.
        if (isSup)
            return (lineY, subH);
        return (lineY + lineH - subH, subH);
    }

    // ── Caret timer ──────────────────────────────────────────────────────────

    private void StartCaretTimer()
    {
        if (_caretTimer != null) return;
        _caretTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _caretTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
        _caretTimer.Start();
    }

    private void StopCaretTimer()
    {
        _caretTimer?.Stop();
        _caretTimer = null;
    }

    private void ResetCaretBlink()
    {
        _caretVisible = true;
        _caretTimer?.Stop();
        _caretTimer?.Start();
        InvalidateVisual();
    }

    /// <summary>
    /// Full-bounds hit target: plain <see cref="Control"/> does not hit-test “empty” space, so clicks would pass through.
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    // ── Focus ────────────────────────────────────────────────────────────────

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _caretVisible = true;
        StartCaretTimer();
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _caretVisible = false;
        InvalidateVisual();
    }

    // ── Pointer input ────────────────────────────────────────────────────────

    /// <summary>
    /// Programmatically begins a drag-select from outside a pointer event (e.g. when the press
    /// landed in the block padding rather than directly over this control). Captures the pointer
    /// so subsequent PointerMoved events update the selection normally.
    /// </summary>
    public void StartDragSelect(int anchorCharIndex, IPointer pointer)
    {
        Focus();
        anchorCharIndex = Math.Clamp(anchorCharIndex, 0, TextLength);
        CaretIndex = anchorCharIndex;
        SelectionStart = anchorCharIndex;
        SelectionEnd = anchorCharIndex;
        _dragAnchor = anchorCharIndex;
        _isDragging = true;
        pointer.Capture(this);
        ResetCaretBlink();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Watermark))
            InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredInlineEquationCharIndex.HasValue)
        {
            _hoveredInlineEquationCharIndex = null;
            InvalidateVisual();
        }
        if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Watermark))
            InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (TryOpenSpellcheckContextMenu(e.GetPosition(this)))
                e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        var idx = HitTestPoint(pos);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && TryGetLinkUrlAt(idx, out var linkUrl)
            && !string.IsNullOrEmpty(linkUrl))
        {
            e.Handled = true;
            if (ExternalLinkNavigationRequested != null)
                _ = ExternalLinkNavigationRequested.Invoke(linkUrl);
            else
                OpenExternalUrl(linkUrl, this);
            return;
        }

        Focus();

        if (e.ClickCount == 1)
        {
            // Single-click must place the caret / start a drag-select. Opening the equation editor here
            // stole the gesture and made selection across inline math unreliable (double-click still opens).
            CaretIndex = idx;
            SelectionStart = idx;
            SelectionEnd = idx;
            _dragAnchor = idx;
            _isDragging = true;
            e.Pointer.Capture(this);
        }
        else if (e.ClickCount == 2)
        {
            if (!IsReadOnly && TryOpenInlineEquationFlyoutAtPoint(pos))
            {
                e.Handled = true;
                return;
            }
            SelectWord(idx);
        }
        else if (e.ClickCount >= 3)
        {
            SelectionStart = 0;
            SelectionEnd = TextLength;
            CaretIndex = TextLength;
        }

        ResetCaretBlink();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateInlineEquationHoverState(e.GetPosition(this));
        var isLeftPressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (!_isDragging)
        {
            if (!isLeftPressed)
                return;
            _dragAnchor = CaretIndex;
            _isDragging = true;
            e.Pointer.Capture(this);
        }

        var pos = e.GetPosition(this);
        var idx = HitTestPoint(pos);
        SelectionStart = Math.Min(_dragAnchor, idx);
        SelectionEnd = Math.Max(_dragAnchor, idx);
        CaretIndex = idx;
        ResetCaretBlink();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
        }
    }

    // ── Keyboard input ───────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Left:
                MoveOrExtend(shift, ctrl
                    ? FindWordStart(_caretIndex - 1)
                    : Math.Max(0, _caretIndex - 1));
                e.Handled = true;
                break;

            case Key.Right:
                MoveOrExtend(shift, ctrl
                    ? FindWordEnd(_caretIndex + 1)
                    : Math.Min(TextLength, _caretIndex + 1));
                e.Handled = true;
                break;

            case Key.Home:
                MoveOrExtend(shift, 0);
                e.Handled = true;
                break;

            case Key.End:
                MoveOrExtend(shift, TextLength);
                e.Handled = true;
                break;

            case Key.Delete:
                if (!IsReadOnly)
                    HandleDelete();
                e.Handled = true;
                break;

            case Key.Back:
                // Let parent tunnel handler deal with merge/block-delete; only handle
                // in-block backspace (caret not at 0, or active selection).
                if (_caretIndex > 0 || HasSelection)
                {
                    if (!IsReadOnly)
                    {
                        if (ctrl && !HasSelection)
                            HandleBackspaceWord();
                        else
                            HandleBackspace();
                    }
                    e.Handled = true;
                }
                break;

            case Key.A when ctrl:
                if (Math.Min(SelectionStart, SelectionEnd) == 0 && Math.Max(SelectionStart, SelectionEnd) == TextLength)
                {
                    // Full text already selected, let parent (BlockEditor) handle it to select all blocks
                    e.Handled = false;
                    break;
                }
                SelectionStart = 0;
                SelectionEnd = TextLength;
                CaretIndex = TextLength;
                e.Handled = true;
                break;

            // Ctrl+C / Ctrl+X / Ctrl+V: owned by BlockEditor tunnel handler (markdown + Mnemo JSON).
        }

        if (e.Handled)
            ResetCaretBlink();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsReadOnly || string.IsNullOrEmpty(e.Text)) return;

        InsertText(e.Text);
        e.Handled = true;
        ResetCaretBlink();
    }

    // ── Editing operations ───────────────────────────────────────────────────

    private bool HasSelection => Math.Min(_selectionStart, _selectionEnd) < Math.Max(_selectionStart, _selectionEnd);

    private void HandleDelete()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (_caretIndex >= TextLength) return;
        DeleteRange(_caretIndex, _caretIndex + 1);
    }

    private void HandleBackspace()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (_caretIndex <= 0) return;
        if (TryUndoLastAutoShortcutConversion())
            return;
        int newCaret = _caretIndex - 1;
        DeleteRange(newCaret, _caretIndex);
        CaretIndex = newCaret;
        SelectionStart = newCaret;
        SelectionEnd = newCaret;
    }

    private void HandleBackspaceWord()
    {
        if (_caretIndex <= 0) return;
        int deleteStart = FindWordStart(_caretIndex - 1);
        if (deleteStart >= _caretIndex) return;
        DeleteRange(deleteStart, _caretIndex);
        CaretIndex = deleteStart;
        SelectionStart = deleteStart;
        SelectionEnd = deleteStart;
    }

    private void DeleteSelection()
    {
        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);
        if (start >= end) return;
        DeleteRange(start, end);
        CaretIndex = start;
        SelectionStart = start;
        SelectionEnd = start;
    }

    /// <summary>Delete characters [start, end) and notify via TextChanged.</summary>
    private void DeleteRange(int start, int end)
    {
        var flat = FlattenRuns(Spans ?? Array.Empty<InlineSpan>());
        if (flat.Length == 0 && start == 0 && end == 1)
        {
            SelectionStart = 0;
            SelectionEnd = 0;
            CaretIndex = 0;
            return;
        }
        var runs = ApplyTextDeletion(Spans ?? Array.Empty<InlineSpan>(), start, end);
        Spans = runs;
    }

    private void InsertText(string text)
    {
        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);
        var currentRuns = Spans ?? Array.Empty<InlineSpan>();
        var oldFlat = FlattenRuns(currentRuns);
        var maxIndex = oldFlat.Length;
        start = Math.Clamp(start, 0, maxIndex);
        end = Math.Clamp(end, 0, maxIndex);
        if (end < start)
            end = start;
        int removeLen = end - start;
        var newFlat = removeLen > 0
            ? oldFlat.Remove(start, removeLen).Insert(start, text)
            : oldFlat.Insert(start, text);
        int newCaret = start + text.Length;

        var shortcuts = ResolveTextShortcutService();
        if (shortcuts != null)
        {
            var result = shortcuts.Apply(newFlat, newCaret, start, text.Length);
            if (result.WasTransformed)
            {
                newFlat = result.Text;
                newCaret = Math.Clamp(result.CaretIndex, 0, newFlat.Length);
                if (!string.IsNullOrEmpty(result.LastAppliedSequence)
                    && !string.IsNullOrEmpty(result.LastAppliedReplacement)
                    && result.LastAppliedStartIndex >= 0)
                {
                    _pendingAutoShortcutUndo = new AutoShortcutUndoState
                    {
                        ReplacementStart = result.LastAppliedStartIndex,
                        Replacement = result.LastAppliedReplacement!,
                        OriginalSequence = result.LastAppliedSequence!,
                        TextLengthAfterConversion = newFlat.Length
                    };
                }
            }
            else
                _pendingAutoShortcutUndo = null;
        }
        else
            _pendingAutoShortcutUndo = null;

        IReadOnlyList<InlineSpan> runs = Core.Formatting.InlineSpanFormatApplier.ApplyTextEdit(currentRuns, oldFlat, newFlat);

        // Sticky sub/sup mode: force the desired flags on the inserted character range.
        if (_pendingInsertStyle is { } pending && text.Length > 0 && newCaret > start)
        {
            runs = Core.Formatting.InlineSpanFormatApplier.ForceSubSup(
                runs, start, newCaret, pending.Subscript, pending.Superscript);
            if (_clearPendingInsertStyleAfterNextInsert)
            {
                _pendingInsertStyle = null;
                _clearPendingInsertStyleAfterNextInsert = false;
            }
        }

        if (TryPromoteFractionAtCaret(runs, newCaret, out var fractionRuns, out var fractionCaret))
        {
            runs = fractionRuns;
            newCaret = fractionCaret;
        }
        Spans = runs;
        CaretIndex = newCaret;
        SelectionStart = newCaret;
        SelectionEnd = newCaret;
    }

    private static ITextShortcutService? _textShortcutService;

    /// <summary>
    /// Resolve lazily so design-time / unit-test instances without a running DI container fall back
    /// to a no-op (returning null) rather than crashing the editor.
    /// </summary>
    private static ITextShortcutService? ResolveTextShortcutService()
    {
        if (_textShortcutService != null) return _textShortcutService;
        if ((Application.Current as App)?.Services is { } sp)
            _textShortcutService = sp.GetService<ITextShortcutService>();
        return _textShortcutService;
    }

    private static ISpellcheckService? ResolveSpellcheckService()
    {
        if (_spellcheckService != null) return _spellcheckService;
        if ((Application.Current as App)?.Services is { } sp)
            _spellcheckService = sp.GetService<ISpellcheckService>();
        return _spellcheckService;
    }

    private static ISettingsService? ResolveSettingsService()
    {
        if (_settingsService != null) return _settingsService;
        if ((Application.Current as App)?.Services is { } sp)
            _settingsService = sp.GetService<ISettingsService>();
        return _settingsService;
    }

    private async Task InitializeSpellcheckAsync()
    {
        if (_spellcheckInitialized)
            return;
        _spellcheckInitialized = true;

        var settings = ResolveSettingsService();
        if (settings == null)
            return;

        _spellcheckEnabled = await settings.GetAsync("Editor.SpellCheck", true).ConfigureAwait(false);
        var languages = await settings.GetAsync("Editor.SpellCheckLanguages", "en").ConfigureAwait(false);
        _spellcheckLanguages = ParseLanguageCodes(languages);
        settings.SettingChanged += OnSettingChanged;
        ScheduleSpellcheck(force: true);
    }

    private async void OnSettingChanged(object? sender, string key)
    {
        if (!SpellcheckSettingKeys.Contains(key, StringComparer.Ordinal))
            return;

        var settings = ResolveSettingsService();
        if (settings == null)
            return;

        _spellcheckEnabled = await settings.GetAsync("Editor.SpellCheck", true).ConfigureAwait(false);
        var languages = await settings.GetAsync("Editor.SpellCheckLanguages", "en").ConfigureAwait(false);
        _spellcheckLanguages = ParseLanguageCodes(languages);
        ScheduleSpellcheck(force: true);
    }

    private void ScheduleSpellcheck(bool force = false)
    {
        if (VisualRoot == null)
            return;
        if (force)
            _lastSpellcheckText = string.Empty;

        _spellcheckDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _spellcheckDebounceTimer.Tick -= OnSpellcheckDebounceTick;
        _spellcheckDebounceTimer.Tick += OnSpellcheckDebounceTick;
        _spellcheckDebounceTimer.Stop();
        _spellcheckDebounceTimer.Start();
    }

    private async void OnSpellcheckDebounceTick(object? sender, EventArgs e)
    {
        _spellcheckDebounceTimer?.Stop();
        await RunSpellcheckAsync().ConfigureAwait(false);
    }

    private async Task RunSpellcheckAsync()
    {
        var service = ResolveSpellcheckService();
        if (service == null || !_spellcheckEnabled)
        {
            ClearSpellcheckIssues();
            return;
        }

        var languages = _spellcheckLanguages.Count == 0 ? ["en"] : _spellcheckLanguages;
        var spans = Spans ?? Array.Empty<InlineSpan>();
        var currentText = Text;
        if (string.Equals(currentText, _lastSpellcheckText, StringComparison.Ordinal))
            return;

        _spellcheckCts?.Cancel();
        _spellcheckCts?.Dispose();
        _spellcheckCts = new CancellationTokenSource();
        try
        {
            var issues = await service.CheckAsync(spans, languages, _spellcheckCts.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_spellcheckSync)
                {
                    _spellcheckIssues.Clear();
                    _spellcheckIssues.AddRange(issues);
                    _lastSpellcheckText = currentText;
                }
                InvalidateVisual();
            });
        }
        catch (OperationCanceledException)
        {
            // Newer text input superseded this run.
        }
    }

    private void ClearSpellcheckIssues()
    {
        lock (_spellcheckSync)
        {
            _spellcheckIssues.Clear();
            _lastSpellcheckText = string.Empty;
        }
        InvalidateVisual();
    }

    private void RenderSpellcheckUnderlines(DrawingContext context)
    {
        if (_textLayout == null || !_spellcheckEnabled)
            return;

        var pen = new Pen(GetSpellcheckUnderlineBrush(), 1.2);
        List<SpellcheckIssue> issues;
        lock (_spellcheckSync)
            issues = [.. _spellcheckIssues];

        foreach (var issue in issues)
        {
            if (issue.Length <= 0)
                continue;
            int logicalStart = Math.Clamp(issue.Start, 0, TextLength);
            int logicalEnd = Math.Clamp(issue.Start + issue.Length, 0, TextLength);
            if (logicalEnd <= logicalStart)
                continue;

            var layoutStart = LogicalCaretToLayoutBoundary(logicalStart);
            var layoutLen = LogicalCaretToLayoutBoundary(logicalEnd) - layoutStart;
            if (layoutLen <= 0)
                continue;

            var rects = _textLayout.HitTestTextRange(layoutStart, layoutLen).ToList();
            foreach (var rect in rects)
            {
                if (rect.Width <= 0.5 || rect.Height <= 0.5)
                    continue;
                var y = rect.Bottom - 1.0;
                context.DrawLine(pen, new Point(rect.X, y), new Point(rect.Right, y));
            }
        }
    }

    private IBrush GetSpellcheckUnderlineBrush()
    {
        if (Application.Current?.TryFindResource("SystemFillColorCriticalBrush", out var resource) == true
            && resource is IBrush brush)
            return brush;
        return new SolidColorBrush(Color.FromRgb(0xE0, 0x45, 0x45));
    }

    private bool TryOpenSpellcheckContextMenu(Point point)
    {
        if (!_spellcheckEnabled || IsReadOnly)
            return false;

        var idx = HitTestPoint(point);
        if (!TryGetIssueAtIndex(idx, out var issue))
            return false;

        var items = new List<object>();
        foreach (var suggestion in issue.Suggestions.Take(5))
        {
            var captured = suggestion;
            var menuItem = new MenuItem
            {
                Header = captured,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            menuItem.Click += (_, _) => ReplaceMisspelling(issue, captured);
            items.Add(menuItem);
        }

        if (items.Count == 0)
            items.Add(new MenuItem { Header = TNotes("NoSuggestions"), IsEnabled = false });

        items.Add(new Separator());
        var addWordItem = new MenuItem
        {
            Header = TNotes("AddWordToDictionary"),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        addWordItem.Click += async (_, _) => await AddWordToSpellbookAsync(issue.Word).ConfigureAwait(false);
        items.Add(addWordItem);

        var menu = new ContextMenu();
        menu.ItemsSource = items;
        menu.Open(this);
        return true;
    }

    private bool TryGetIssueAtIndex(int index, out SpellcheckIssue issue)
    {
        lock (_spellcheckSync)
        {
            issue = _spellcheckIssues.FirstOrDefault(i => index >= i.Start && index < i.Start + i.Length)
                ?? new SpellcheckIssue(0, 0, string.Empty, []);
            return issue.Length > 0;
        }
    }

    private void ReplaceMisspelling(SpellcheckIssue issue, string replacement)
    {
        SelectionStart = issue.Start;
        SelectionEnd = issue.Start + issue.Length;
        CaretIndex = SelectionEnd;
        InsertText(replacement);
        ScheduleSpellcheck(force: true);
    }

    private async Task AddWordToSpellbookAsync(string word)
    {
        var service = ResolveSpellcheckService();
        if (service == null)
            return;

        try
        {
            await service.AddWordAsync(word, _spellcheckLanguages, CancellationToken.None).ConfigureAwait(false);
            ScheduleSpellcheck(force: true);
        }
        catch (Exception)
        {
            // Keep editor responsive even if custom dictionary write fails.
        }
    }

    private static List<string> ParseLanguageCodes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ["en"];

        var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => value.Replace('_', '-'))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count == 0 ? ["en"] : values;
    }

    private static bool TryPromoteFractionAtCaret(
        IReadOnlyList<InlineSpan> runs,
        int caretIndex,
        out IReadOnlyList<InlineSpan> promotedRuns,
        out int promotedCaret)
    {
        promotedRuns = runs;
        promotedCaret = caretIndex;

        var flat = InlineSpanText.FlattenEditing(runs);
        if (caretIndex <= 0 || caretIndex > flat.Length)
            return false;

        var windowStart = Math.Max(0, caretIndex - 24);
        var tail = flat.Substring(windowStart, caretIndex - windowStart);
        var match = TailFractionTokenRegex.Match(tail);
        if (!match.Success)
            return false;

        var token = match.Value;
        if (!FractionShortcutResolver.TryParse(token, out var numerator, out var denominator))
            return false;

        var tokenStart = windowStart + match.Index;
        var tokenEnd = tokenStart + token.Length;
        var insertion = new List<InlineSpan> { new FractionSpan(numerator, denominator) };
        promotedRuns = Core.Formatting.InlineSpanFormatApplier.ReplaceRange(runs, tokenStart, tokenEnd, insertion);
        promotedCaret = tokenStart + 1;
        return true;
    }

    private bool TryUndoLastAutoShortcutConversion()
    {
        var pending = _pendingAutoShortcutUndo;
        if (pending is null)
            return false;

        if (_caretIndex != pending.ReplacementStart + pending.Replacement.Length
            || TextLength != pending.TextLengthAfterConversion
            || pending.ReplacementStart < 0
            || pending.ReplacementStart + pending.Replacement.Length > TextLength)
        {
            _pendingAutoShortcutUndo = null;
            return false;
        }

        var currentRuns = Spans ?? Array.Empty<InlineSpan>();
        var oldFlat = FlattenRuns(currentRuns);
        if (!oldFlat.AsSpan(pending.ReplacementStart, pending.Replacement.Length).SequenceEqual(pending.Replacement.AsSpan()))
        {
            _pendingAutoShortcutUndo = null;
            return false;
        }

        var newFlat = oldFlat.Remove(pending.ReplacementStart, pending.Replacement.Length)
            .Insert(pending.ReplacementStart, pending.OriginalSequence);
        var newRuns = Core.Formatting.InlineSpanFormatApplier.ApplyTextEdit(currentRuns, oldFlat, newFlat);
        var newCaret = pending.ReplacementStart + pending.OriginalSequence.Length;

        Spans = newRuns;
        CaretIndex = newCaret;
        SelectionStart = newCaret;
        SelectionEnd = newCaret;
        _pendingAutoShortcutUndo = null;
        return true;
    }

    /// <summary>Inserts text at the caret (replacing selection), same as typed input.</summary>
    public void InsertTextAtCaret(string text) => InsertText(text);

    /// <summary>
    /// Activates sticky sub/sup typing mode: subsequent insertions are forced to
    /// <paramref name="subscript"/>/<paramref name="superscript"/> regardless of adjacent span style.
    /// Pass both <c>false</c> to escape from an active sub/sup span (disables one insertion then auto-clears).
    /// Pass <c>null</c> to fully disable the override and resume natural style inheritance.
    /// </summary>
    internal void SetPendingSubSup(bool? subscript, bool? superscript)
    {
        if (subscript == null && superscript == null)
        {
            _pendingInsertStyle = null;
            _clearPendingInsertStyleAfterNextInsert = false;
            InvalidateVisual();
            return;
        }

        bool sub = subscript ?? false;
        bool sup = superscript ?? false;

        if (!sub && !sup)
        {
            // Escape mode: force-clear sub/sup for exactly one insertion, then return to natural.
            _pendingInsertStyle = Core.Models.TextStyle.Default;
            _clearPendingInsertStyleAfterNextInsert = true;
            InvalidateVisual();
        }
        else
        {
            _pendingInsertStyle = new Core.Models.TextStyle(Subscript: sub, Superscript: sup);
            _clearPendingInsertStyleAfterNextInsert = false;
            InvalidateVisual();
        }
    }

    // ── Run mutation helpers ─────────────────────────────────────────────────

    private static IReadOnlyList<InlineSpan> ApplyTextDeletion(
        IReadOnlyList<InlineSpan> runs, int start, int end)
    {
        return Core.Formatting.InlineSpanFormatApplier.ApplyTextEdit(
            runs, FlattenRuns(runs), FlattenRuns(runs).Remove(start, end - start));
    }

    private static IReadOnlyList<InlineSpan> ApplyTextInsertion(
        IReadOnlyList<InlineSpan> runs, int selStart, int selEnd, string text)
    {
        var flat = FlattenRuns(runs);
        int removeLen = selEnd - selStart;
        var newFlat = removeLen > 0
            ? flat.Remove(selStart, removeLen).Insert(selStart, text)
            : flat.Insert(selStart, text);
        return Core.Formatting.InlineSpanFormatApplier.ApplyTextEdit(runs, flat, newFlat);
    }

    private static string FlattenRuns(IReadOnlyList<InlineSpan> runs) =>
        InlineSpanText.FlattenEditing(runs);

    // ── Hit-testing ──────────────────────────────────────────────────────────

    /// <summary>Returns true if <paramref name="index"/> falls inside a linked run; outputs the href.</summary>
    public bool TryGetLinkUrlAt(int index, out string? url)
    {
        url = null;
        var runs = Spans ?? Array.Empty<InlineSpan>();
        if (runs.Count == 0) return false;
        int len = TextLength;
        if (len == 0) return false;
        int i = Math.Clamp(index, 0, len - 1);
        int pos = 0;
        foreach (var seg in runs)
        {
            int segLen = seg is TextSpan t ? t.Text.Length : 1;
            int end = pos + segLen;
            if (i < end && i >= pos)
            {
                url = seg switch
                {
                    TextSpan tx => tx.Style.LinkUrl,
                    EquationSpan eq => eq.Style.LinkUrl,
                    FractionSpan fr => fr.Style.LinkUrl,
                    _ => null
                };
                return url != null;
            }

            pos = end;
        }

        return false;
    }

    /// <summary>Opens http(s) or mailto in the system browser / mail client.</summary>
    public static void OpenExternalUrl(string url, Control? anchor)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;
        try
        {
            var top = anchor != null ? TopLevel.GetTopLevel(anchor) : null;
            if (top?.Launcher != null)
            {
                _ = top.Launcher.LaunchUriAsync(uri);
                return;
            }
        }
        catch
        {
            // fall through to shell
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    public int HitTestPoint(Point point)
    {
        // Reuse existing width-sync guard so pointer hit-tests use a layout matching rendered width.
        EnsureLayoutForVerticalNavigation();

        if (_textLayout is null) return 0;
        var local = new Point(point.X - _mathPadLeft, point.Y - _mathPadTop);
        if (TryClampHitToLineEdge(local, out var clamped))
            return clamped;
        try
        {
            var result = _textLayout.HitTestPoint(local);
            int rawLayoutBoundary = result.IsTrailing ? result.TextPosition + 1 : result.TextPosition;
            int layoutBoundary = Math.Clamp(rawLayoutBoundary, 0, _layoutTextLength);
            int logical = LayoutBoundaryIndexToLogicalCaret(layoutBoundary);
            return Math.Clamp(logical, 0, TextLength);
        }
        catch
        {
            return HitTestPointFallback(local);
        }
    }

    /// <summary>
    /// If the pointer is clearly to the left/right of a visual line, clamp to the line's start/end caret.
    /// This avoids relying on <see cref="TextLayout.HitTestPoint"/> behaviour for far-off whitespace (which can vary
    /// and may return a non-edge character without throwing).
    /// </summary>
    private bool TryClampHitToLineEdge(Point local, out int logicalCaret)
    {
        logicalCaret = 0;
        if (_textLayout == null)
            return false;

        var lines = _textLayout.TextLines;
        if (lines.Count == 0)
            return false;

        var (lineIndex, _) = GetLineIndexAtY(lines, local.Y);
        var line = lines[Math.Clamp(lineIndex, 0, lines.Count - 1)];

        int lineStart = line.FirstTextSourceIndex;
        int lineEnd = lineStart + LineContentCharCount(line);
        var (lineMinX, lineMaxX) = GetLineHorizontalHitBounds(line, lineStart, lineEnd);

        const double edgeSlop = 1.0;
        if (local.X >= -edgeSlop && local.X <= lineMaxX + edgeSlop)
            return false;

        int layoutBoundary = local.X < 0 ? lineStart : lineEnd;
        layoutBoundary = Math.Clamp(layoutBoundary, 0, _layoutTextLength);
        logicalCaret = Math.Clamp(LayoutBoundaryIndexToLogicalCaret(layoutBoundary), 0, TextLength);
        return true;
    }

    private static (int LineIndex, double LineTop) GetLineIndexAtY(IReadOnlyList<TextLine> lines, double y)
    {
        if (lines.Count == 0)
            return (0, 0);
        if (y <= 0)
            return (0, 0);

        double top = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var bottom = top + line.Height;
            if (y <= bottom)
                return (i, top);
            top = bottom;
        }
        return (lines.Count - 1, Math.Max(0, top - lines[^1].Height));
    }

    /// <summary>
    /// Robust fallback when <see cref="TextLayout.HitTestPoint"/> throws for points outside glyph geometry.
    /// Maps to the nearest caret edge on the nearest visual line so drag-start from block padding still works.
    /// </summary>
    private int HitTestPointFallback(Point local)
    {
        if (_textLayout == null)
            return 0;

        var lines = _textLayout.TextLines;
        if (lines.Count == 0)
            return Math.Clamp(local.X <= 0 ? 0 : TextLength, 0, TextLength);

        var (lineIndex, _) = GetLineIndexAtY(lines, local.Y);

        var targetLine = lines[lineIndex];
        int lineStart = targetLine.FirstTextSourceIndex;
        int lineEnd = lineStart + LineContentCharCount(targetLine);
        int layoutBoundary = local.X <= 0 ? lineStart : lineEnd;
        layoutBoundary = Math.Clamp(layoutBoundary, 0, _layoutTextLength);
        return Math.Clamp(LayoutBoundaryIndexToLogicalCaret(layoutBoundary), 0, TextLength);
    }

    private (double MinX, double MaxX) GetLineHorizontalHitBounds(TextLine line, int lineStart, int lineEnd)
    {
        // Use line-local hit-test bounds instead of [0..width] so leading visual slack before the first
        // rendered glyph still maps to "before first character" instead of an unstable in-line hit.
        var minX = 0.0;
        var maxX = line.WidthIncludingTrailingWhitespace;
        if (double.IsNaN(maxX) || double.IsInfinity(maxX) || maxX <= 0)
            maxX = Math.Max(line.Width, 0);

        if (_textLayout == null)
            return (minX, maxX);

        try
        {
            var startRect = _textLayout.HitTestTextPosition(Math.Clamp(lineStart, 0, _layoutTextLength));
            minX = startRect.X;
            maxX = minX + Math.Max(maxX, 0);
        }
        catch
        {
            // Keep conservative fallback bounds.
        }

        return (minX, maxX);
    }

    /// <summary>
    /// Union of layout span rects and painted math ink, in text-layout local coordinates (inside math padding transform).
    /// </summary>
    private bool TryGetInlineEquationBounds(InlineEquationEntry eq, double pad, out Rect bounds)
    {
        bounds = default;
        if (_textLayout == null || _layoutBoundaryAtLogical == null)
            return false;

        int lo = LogicalCaretToLayoutBoundary(eq.CharIndex);
        int hi = LogicalCaretToLayoutBoundary(eq.CharIndex + 1);
        int spanLen = hi - lo;
        if (spanLen <= 0)
            return false;

        try
        {
            var rects = _textLayout.HitTestTextRange(lo, spanLen).ToList();
            if (rects.Count == 0)
            {
                var p = _textLayout.HitTestTextPosition(lo);
                bounds = new Rect(p.X, p.Y, Math.Max(p.Width, 1), p.Height);
            }
            else
            {
                bounds = rects[0];
                for (int i = 1; i < rects.Count; i++)
                    bounds = bounds.Union(rects[i]);
            }
        }
        catch
        {
            return false;
        }

        if (eq.Layout != null)
        {
            try
            {
                var leftX = _textLayout.HitTestTextPosition(lo).X;
                var baselineY = GetTextBaselineYForLayoutIndex(_textLayout, lo);
                var ink = CalculateMathPaintBounds(eq.Layout, leftX, baselineY);
                bounds = bounds.Union(ink);
            }
            catch
            {
                // keep layout-only bounds
            }
        }

        bounds = new Rect(bounds.X - pad, bounds.Y - pad, bounds.Width + 2 * pad, bounds.Height + 2 * pad);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    /// <summary>
    /// Notion-like: hit target is the union of the reserved layout span and the painted math ink (with padding),
    /// not only layout character boundaries (which missed trailing space and tall fractions).
    /// </summary>
    private bool TryOpenInlineEquationFlyoutAtPoint(Point pos)
    {
        if (_textLayout == null || _layoutBoundaryAtLogical == null)
            return false;

        var local = new Point(pos.X - _mathPadLeft, pos.Y - _mathPadTop);

        foreach (var eq in _inlineEquations)
        {
            const double pad = 3;
            if (!TryGetInlineEquationBounds(eq, pad, out var bounds))
                continue;

            if (bounds.Contains(local))
            {
                OpenInlineEquationFlyout(eq.CharIndex, eq.Latex ?? string.Empty);
                return true;
            }
        }

        return false;
    }

    private void UpdateInlineEquationHoverState(Point pos)
    {
        if (!ShowInlineEquationSourceOnHover || _textLayout == null || _layoutBoundaryAtLogical == null)
        {
            if (_hoveredInlineEquationCharIndex.HasValue)
            {
                _hoveredInlineEquationCharIndex = null;
                InvalidateVisual();
            }
            return;
        }

        var local = new Point(pos.X - _mathPadLeft, pos.Y - _mathPadTop);
        int? hovered = null;
        foreach (var eq in _inlineEquations)
        {
            if (TryGetInlineEquationBounds(eq, pad: 2, out var bounds) && bounds.Contains(local))
            {
                hovered = eq.CharIndex;
                break;
            }
        }

        if (_hoveredInlineEquationCharIndex == hovered)
            return;

        _hoveredInlineEquationCharIndex = hovered;
        InvalidateVisual();
    }

    // ── Word nav helpers ─────────────────────────────────────────────────────

    private void MoveOrExtend(bool extend, int newPos)
    {
        if (extend)
        {
            SelectionEnd = newPos;
            CaretIndex = newPos;
        }
        else
        {
            if (HasSelection && !extend)
            {
                // Collapse to the near end of the selection
                int sel = newPos < _caretIndex
                    ? Math.Min(_selectionStart, _selectionEnd)
                    : Math.Max(_selectionStart, _selectionEnd);
                CaretIndex = sel;
            }
            else
            {
                CaretIndex = newPos;
            }
            SelectionStart = CaretIndex;
            SelectionEnd = CaretIndex;
        }
    }

    /// <summary>
    /// Up/Down to the adjacent <see cref="TextLayout"/> line (soft wrap and hard newlines).
    /// Returns false on the first/last visual line so the block editor can move focus.
    /// </summary>
    public bool TryVerticalLogicalNavigation(bool shift, bool up)
    {
        if (!TryMoveCaretOneVisualLine(up, out var newPos))
            return false;
        MoveOrExtend(shift, newPos);
        ResetCaretBlink();
        return true;
    }

    /// <summary>
    /// Horizontal center of the caret in layout coordinates, for matching column when moving to another block.
    /// </summary>
    public bool TryGetCaretHorizontalOffsetForBlockNavigation(out double pixelX)
    {
        pixelX = 0;
        EnsureLayoutForVerticalNavigation();
        var layout = _textLayout;
        if (layout == null)
            return false;

        if (TextLength == 0)
        {
            try
            {
                var r = layout.HitTestTextPosition(0);
                pixelX = r.Width > 0.01 ? r.X + r.Width * 0.5 : r.X + 1;
            }
            catch
            {
                pixelX = 0;
            }
            return true;
        }

        var hitPos = LogicalCaretToLayoutBoundary(Math.Clamp(_caretIndex, 0, TextLength));
        try
        {
            var caretRect = layout.HitTestTextPosition(hitPos);
            pixelX = caretRect.Width > 0.01
                ? caretRect.X + caretRect.Width * 0.5
                : caretRect.X + 1;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Places the caret on the first or last visual line at the given horizontal offset (same convention as
    /// <see cref="TryGetCaretHorizontalOffsetForBlockNavigation"/>).
    /// </summary>
    public int GetCaretIndexFromHorizontalOffset(double pixelX, bool useFirstVisualLine)
    {
        EnsureLayoutForVerticalNavigation();
        var layout = _textLayout;
        if (layout == null)
            return 0;

        var lines = layout.TextLines;
        if (lines.Count == 0)
            return Math.Clamp(_caretIndex, 0, TextLength);

        int lineIdx = useFirstVisualLine ? 0 : lines.Count - 1;
        var targetTextLine = lines[lineIdx];
        var yTop = GetAccumulatedLineTop(layout, lineIdx);
        var probeY = yTop + targetTextLine.Height * 0.5;
        var maxX = Math.Max(targetTextLine.WidthIncludingTrailingWhitespace, 1);
        var probeX = Math.Clamp(pixelX, 0, maxX);
        var idx = HitTestLayoutAt(layout, new Point(probeX, probeY));
        return Math.Clamp(idx, 0, TextLength);
    }

    private void EnsureLayoutForVerticalNavigation()
    {
        var effectiveW = ComputeEffectiveLayoutMaxWidth();
        var needRebuild = _textLayout == null || Text != _lastBuiltText;
        if (!needRebuild && _textLayout != null
            && Math.Abs(effectiveW - _textLayout.MaxWidth) > 0.5)
            needRebuild = true;
        if (needRebuild)
            BuildLayout(effectiveW);
    }

    /// <summary>
    /// Moves the caret to the same horizontal aim on the adjacent visual line.
    /// </summary>
    private bool TryMoveCaretOneVisualLine(bool up, out int newCaretIndex)
    {
        newCaretIndex = _caretIndex;
        EnsureLayoutForVerticalNavigation();
        var layout = _textLayout;
        if (layout == null)
            return false;

        var lines = layout.TextLines;
        if (lines.Count == 0 || TextLength == 0)
            return false;

        var trailingEdge = _caretIndex >= TextLength;
        var idxForLineLookup = LogicalCaretToLayoutBoundary(Math.Clamp(_caretIndex, 0, TextLength));
        var oldLine = layout.GetLineIndexFromCharacterIndex(idxForLineLookup, trailingEdge);

        if (up)
        {
            if (oldLine <= 0)
                return false;
        }
        else if (oldLine >= lines.Count - 1)
        {
            return false;
        }

        var targetVisualLine = up ? oldLine - 1 : oldLine + 1;
        var targetTextLine = lines[targetVisualLine];
        var yTop = GetAccumulatedLineTop(layout, targetVisualLine);
        var probeY = yTop + targetTextLine.Height * 0.5;

        var hitPos = LogicalCaretToLayoutBoundary(Math.Clamp(_caretIndex, 0, TextLength));
        Rect caretRect;
        try
        {
            caretRect = layout.HitTestTextPosition(hitPos);
        }
        catch
        {
            caretRect = default;
        }

        var probeX = caretRect.Width > 0.01
            ? caretRect.X + caretRect.Width * 0.5
            : caretRect.X + 1;
        var maxX = Math.Max(targetTextLine.WidthIncludingTrailingWhitespace, 1);
        probeX = Math.Clamp(probeX, 0, maxX);

        newCaretIndex = HitTestLayoutAt(layout, new Point(probeX, probeY));

        var layoutIdxForNewLine = LogicalCaretToLayoutBoundary(Math.Clamp(newCaretIndex, 0, TextLength));
        var newLine = layout.GetLineIndexFromCharacterIndex(layoutIdxForNewLine, newCaretIndex >= TextLength);

        if (newLine != targetVisualLine)
        {
            var layoutCaretOld = LogicalCaretToLayoutBoundary(Math.Clamp(_caretIndex, 0, TextLength));
            var col = Math.Max(0, layoutCaretOld - lines[oldLine].FirstTextSourceIndex);
            var layoutCaret = FallbackCaretSameColumn(lines[targetVisualLine], col);
            newCaretIndex = LayoutBoundaryIndexToLogicalCaret(Math.Clamp(layoutCaret, 0, _layoutTextLength));
        }

        newCaretIndex = Math.Clamp(newCaretIndex, 0, TextLength);
        return true;
    }

    private static double GetAccumulatedLineTop(TextLayout layout, int lineIndex)
    {
        double y = 0;
        for (var i = 0; i < lineIndex; i++)
            y += layout.TextLines[i].Height;
        return y;
    }

    private int HitTestLayoutAt(TextLayout layout, Point point)
    {
        try
        {
            var result = layout.HitTestPoint(point);
            int layoutBoundary = result.IsTrailing ? result.TextPosition + 1 : result.TextPosition;
            layoutBoundary = Math.Clamp(layoutBoundary, 0, _layoutTextLength);
            return Math.Clamp(LayoutBoundaryIndexToLogicalCaret(layoutBoundary), 0, TextLength);
        }
        catch
        {
            return _caretIndex;
        }
    }

    /// <summary>Character count on the line excluding mandatory newline at end, if any.</summary>
    private static int LineContentCharCount(TextLine line) =>
        Math.Max(0, line.Length - line.NewLineLength);

    private static int FallbackCaretSameColumn(TextLine line, int columnFromLineStart)
    {
        var start = line.FirstTextSourceIndex;
        var content = LineContentCharCount(line);
        var off = Math.Clamp(columnFromLineStart, 0, content);
        return start + off;
    }

    private static bool IsEquationPlaceholderChar(char c) =>
        c == InlineSpan.EquationAtomChar || c == InlineSpan.FractionAtomChar;

    private int FindWordStart(int pos)
    {
        var text = Text;
        pos = Math.Clamp(pos, 0, text.Length);
        if (pos < text.Length && IsEquationPlaceholderChar(text[pos]))
            return pos;
        if (pos > 0 && IsEquationPlaceholderChar(text[pos - 1]))
            return pos - 1;
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1]))
        {
            if (IsEquationPlaceholderChar(text[pos - 1]))
                return pos - 1;
            pos--;
        }

        return pos;
    }

    private int FindWordEnd(int pos)
    {
        var text = Text;
        pos = Math.Clamp(pos, 0, text.Length);
        if (pos < text.Length && IsEquationPlaceholderChar(text[pos]))
            return pos + 1;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos]))
        {
            if (IsEquationPlaceholderChar(text[pos]))
                return pos + 1;
            pos++;
        }

        return pos;
    }

    /// <summary>
    /// Non-empty [start, end) span of the word at the caret, or null if the caret is on whitespace only.
    /// Used for format shortcuts when there is no active selection.
    /// </summary>
    public (int Start, int End)? TryGetWordRangeAtCaret()
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0) return null;

        int idx = Math.Clamp(_caretIndex, 0, text.Length);

        if (idx < text.Length && char.IsWhiteSpace(text[idx]))
            return null;

        if (idx == text.Length)
        {
            if (!char.IsWhiteSpace(text[idx - 1]))
                idx = idx - 1;
            else
                return null;
        }

        int start = FindWordStart(idx);
        int end = FindWordEnd(idx);
        if (start >= end) return null;
        return (start, end);
    }

    private void SelectWord(int pos)
    {
        SelectionStart = FindWordStart(pos);
        SelectionEnd = FindWordEnd(pos);
        CaretIndex = SelectionEnd;
    }

    // ── Inline equation rendering ────────────────────────────────────────────

    private ILaTeXEngine? ResolveLatexEngine()
    {
        if (_latexEngine != null) return _latexEngine;
        if ((Application.Current as App)?.Services is { } sp)
            _latexEngine = sp.GetService<ILaTeXEngine>();
        return _latexEngine;
    }

    private async Task RebuildInlineEquationsAsync()
    {
        if (!_inlineEquationsDirty) return;
        _inlineEquationsDirty = false;

        var engine = ResolveLatexEngine();
        var built = new List<InlineEquationEntry>();
        var lineLayoutCap = _lastLayoutWidth > 0 ? _lastLayoutWidth : (Bounds.Width > 0 ? Bounds.Width : MinLayoutWidth);

        var runs = Spans ?? Array.Empty<InlineSpan>();
        int charOffset = 0;
        foreach (var seg in runs)
        {
            if (seg is EquationSpan eq && engine != null)
            {
                var latexForLayout = eq.Latex;
                if (_inlineEqPreviewActive && charOffset == _inlineEqCharIndex)
                    latexForLayout = _inlineEqPreviewLatex ?? latexForLayout;

                var entry = new InlineEquationEntry
                {
                    CharIndex = charOffset,
                    Latex = latexForLayout ?? string.Empty
                };

                try
                {
                    var latex = (latexForLayout ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(latex))
                    {
                        built.Add(entry);
                        charOffset += 1;
                        continue;
                    }

                    var boxObj = await engine.GetLayoutBoxAsync(latex, FontSize).ConfigureAwait(true);
                    if (boxObj is Box box)
                    {
                        entry.Layout = box;
                        var advance = box.Width + 2;
                        entry.Width = ClampInlineEquationReserveWidth(advance, latex, FontSize, lineLayoutCap);
                        entry.Height = box.TotalHeight + 2;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Inline equation layout error: {ex.Message}");
                }

                built.Add(entry);
                charOffset += 1;
                continue;
            }

            charOffset += seg is TextSpan t ? t.Text.Length : 1;
        }

        _inlineEquations.Clear();
        _inlineEquations.AddRange(built);

        var layoutWidth = _lastLayoutWidth > 0 ? _lastLayoutWidth : (Bounds.Width > 0 ? Bounds.Width : MinLayoutWidth);
        BuildLayout(layoutWidth);
        ComputeMathPadding();
        InvalidateVisual();

        if (_inlineEquationsDirty)
            _ = RebuildInlineEquationsAsync();
    }

    /// <summary>
    /// When the LaTeX layout engine over-reserves horizontal space (bad glue, delimiter scaling, etc.), the inline
    /// flow still uses <see cref="Box.Width"/> for space runs — clamp so reserve tracks plausible size for the source string.
    /// </summary>
    private static double ClampInlineEquationReserveWidth(double advance, string latex, double fontSize, double lineMaxWidth)
    {
        if (advance <= 0 || double.IsNaN(advance) || double.IsInfinity(advance))
            return Math.Max(fontSize * 0.5, 1);

        var t = (latex ?? string.Empty).Trim();
        if (t.Length == 0)
            return advance;

        if (lineMaxWidth > 0 && advance > lineMaxWidth)
            advance = lineMaxWidth;

        // Long sources may be wide matrices / macros; only apply the heuristic on shorter inline TeX.
        if (t.Length > 96)
            return advance;

        double lengthSlack = t.Length * fontSize * 0.82 + fontSize * 12;
        if (advance <= lengthSlack * 1.45)
            return advance;

        return Math.Min(advance, lengthSlack);
    }

    /// <summary>Union of box ink bounds (same convention as <see cref="LaTeXRenderer"/>).</summary>
    private static Rect CalculateMathPaintBounds(Box box, double x, double baselineY)
    {
        var top = baselineY - box.Height;
        var bounds = new Rect(x, top, box.Width, box.TotalHeight);
        foreach (var (child, cx, cy) in box.GetChildPositions(x, baselineY))
            bounds = bounds.Union(CalculateMathPaintBounds(child, cx, cy));
        return bounds;
    }

    private void ComputeMathPadding()
    {
        if (_textLayout == null)
        {
            SetMathPadsIfChanged(0, 0, 0, 0);
            return;
        }

        const double safety = 2;
        double padLeft = 0, padTop = 0, padRight = 0, padBottom = 0;
        var textH = _textLayout.Height;
        // Include trailing whitespace so pads are not inflated when inline math reserves space runs at line end.
        var textW = Math.Max(_textLayout.Width, _textLayout.WidthIncludingTrailingWhitespace);

        foreach (var eq in _inlineEquations)
        {
            if (eq.Layout == null) continue;
            try
            {
                var layoutEq = LogicalCaretToLayoutBoundary(eq.CharIndex);
                var charRect = _textLayout.HitTestTextPosition(layoutEq);
                var x = charRect.X;
                var baselineY = GetTextBaselineYForLayoutIndex(_textLayout, layoutEq);
                var b = CalculateMathPaintBounds(eq.Layout, x, baselineY);
                padLeft = Math.Max(padLeft, Math.Max(0, safety - b.Left));
                padTop = Math.Max(padTop, Math.Max(0, safety - b.Top));
                padRight = Math.Max(padRight, Math.Max(0, b.Right + safety - textW));
                padBottom = Math.Max(padBottom, Math.Max(0, b.Bottom + safety - textH));
            }
            catch
            {
                // Hit test may fail for edge indices
            }
        }

        SetMathPadsIfChanged(padLeft, padTop, padRight, padBottom);
    }

    private void SetMathPadsIfChanged(double l, double t, double r, double b)
    {
        if (Math.Abs(_mathPadLeft - l) < 0.25 && Math.Abs(_mathPadTop - t) < 0.25
            && Math.Abs(_mathPadRight - r) < 0.25 && Math.Abs(_mathPadBottom - b) < 0.25)
            return;
        _mathPadLeft = l;
        _mathPadTop = t;
        _mathPadRight = r;
        _mathPadBottom = b;
        InvalidateMeasure();
    }

    private void RestartDebouncedInlineEquationRebuild()
    {
        if (_inlineEqRebuildTimer == null)
        {
            _inlineEqRebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _inlineEqRebuildTimer.Tick += (_, _) =>
            {
                _inlineEqRebuildTimer?.Stop();
                if (!_inlineEqPreviewActive) return;
                _inlineEquationsDirty = true;
                _ = RebuildInlineEquationsAsync();
            };
        }

        _inlineEqRebuildTimer.Stop();
        _inlineEqRebuildTimer.Start();
    }

    private void OnInlineEquationFlyoutClosed()
    {
        _inlineEqPreviewActive = false;
        _inlineEqRebuildTimer?.Stop();
        var commit = !_inlineEqSuppressCloseCommit;
        _inlineEqSuppressCloseCommit = false;
        if (commit)
            InlineEquationEdited?.Invoke(_inlineEqCharIndex, _inlineEqTextBox?.Text ?? string.Empty);
        _inlineEqPreviewLatex = null;
        _inlineEquationsDirty = true;
        InvalidateVisual();
        _ = RebuildInlineEquationsAsync();
    }

    /// <summary>
    /// Raised when the inline equation flyout closes with a committed edit (Enter or light dismiss).
    /// Escape closes without raising. Preview text while typing does not touch <see cref="SpansProperty"/>.
    /// Args: (charIndex, newLatex).
    /// </summary>
    public event Action<int, string>? InlineEquationEdited;

    private Flyout? _inlineEqFlyout;
    private TextBox? _inlineEqTextBox;
    private Button? _inlineEqDoneButton;
    private int _inlineEqCharIndex;
    /// <summary>Local (to this control) bounds for anchoring the inline flyout under the equation.</summary>
    private Rect _inlineFlyoutAnchorLocal;

    private void OpenInlineEquationFlyout(int charIndex, string currentLatex)
    {
        _inlineEqCharIndex = charIndex;
        _inlineEqSuppressCloseCommit = false;
        _inlineEqPreviewActive = true;
        _inlineEqPreviewLatex = currentLatex;

        CaretIndex = charIndex;
        SelectionStart = charIndex;
        SelectionEnd = charIndex;

        if (_inlineEqFlyout == null)
        {
            _inlineEqTextBox = new TextBox
            {
                MinWidth = 220,
                MaxWidth = 360,
                AcceptsReturn = false,
                FontFamily = MonoFont,
                FontSize = 14,
                Watermark = TNotes("EquationFlyoutPlaceholder")
            };

            _inlineEqTextBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _inlineEqSuppressCloseCommit = true;
                    _inlineEqFlyout?.Hide();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    _inlineEqSuppressCloseCommit = false;
                    _inlineEqFlyout?.Hide();
                    e.Handled = true;
                }
            };

            _inlineEqTextBox.TextChanged += (_, _) =>
            {
                if (_inlineEqTextBox == null || !_inlineEqPreviewActive) return;
                _inlineEqPreviewLatex = _inlineEqTextBox.Text ?? string.Empty;
                _inlineEquationsDirty = true;
                RestartDebouncedInlineEquationRebuild();
            };

            _inlineEqDoneButton = new Button
            {
                Classes = { "accent" },
                VerticalAlignment = VerticalAlignment.Stretch,
                MinWidth = 80,
                Padding = new Thickness(12, 6),
                Content = $"{TNotes("Done")} \u21B5"
            };
            _inlineEqDoneButton.Click += (_, _) =>
            {
                _inlineEqSuppressCloseCommit = false;
                _inlineEqFlyout?.Hide();
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                MinWidth = 280,
                MaxWidth = 440
            };
            Grid.SetColumn(_inlineEqTextBox, 0);
            Grid.SetColumn(_inlineEqDoneButton, 1);
            _inlineEqTextBox.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(_inlineEqTextBox);
            grid.Children.Add(_inlineEqDoneButton);

            var shell = new Border
            {
                Padding = new Thickness(12, 10),
                CornerRadius = new CornerRadius(8),
                Child = grid
            };
            shell.AttachedToVisualTree += (_, _) =>
            {
                if (shell.TryFindResource("MenuFlyoutPresenterBackground", out var bg) && bg is IBrush bgb)
                    shell.Background = bgb;
                if (shell.TryFindResource("MenuFlyoutPresenterBorderBrush", out var bd) && bd is IBrush bdb)
                    shell.BorderBrush = bdb;
                shell.BorderThickness = new Thickness(1);
            };

            _inlineEqFlyout = new Flyout
            {
                Content = shell,
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = OnInlineEquationFlyoutPlacement,
                ShowMode = FlyoutShowMode.Standard,
                // App sets PopupFlyoutBase default VerticalOffset; custom placement uses p.Offset only.
                VerticalOffset = 0,
                HorizontalOffset = 0
            };

            _inlineEqFlyout.Closed += (_, _) => OnInlineEquationFlyoutClosed();

            _inlineEqFlyout.Opened += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _inlineEqTextBox?.Focus();
                    _inlineEqTextBox?.SelectAll();
                }, DispatcherPriority.Input);
            };
        }
        else
            _inlineEqDoneButton!.Content = $"{TNotes("Done")} \u21B5";

        EnsureLayoutForVerticalNavigation();
        if (_textLayout != null)
        {
            try
            {
                var layoutIdx = LogicalCaretToLayoutBoundary(charIndex);
                var charRect = _textLayout.HitTestTextPosition(layoutIdx);
                double w = LogicalCaretToLayoutBoundary(charIndex + 1) - layoutIdx;
                if (w <= 0.5)
                {
                    foreach (var eq in _inlineEquations)
                    {
                        if (eq.CharIndex == charIndex && eq.Width > 0)
                        {
                            w = Math.Max(w, eq.Width);
                            break;
                        }
                    }
                    w = Math.Max(w, charRect.Width);
                }
                w = Math.Max(w, 8);
                _inlineFlyoutAnchorLocal = new Rect(charRect.X, charRect.Y, w, Math.Max(charRect.Height, 1));
            }
            catch
            {
                _inlineFlyoutAnchorLocal = new Rect(0, 0, Math.Max(Bounds.Width, 8), Math.Max(Bounds.Height, 1));
            }
        }
        else
            _inlineFlyoutAnchorLocal = new Rect(0, 0, Math.Max(Bounds.Width, 8), Math.Max(Bounds.Height, 1));

        _inlineEqTextBox!.Text = currentLatex;
        InvalidateVisual();
        _inlineEqFlyout.ShowAt(this);
    }

    private void OnInlineEquationFlyoutPlacement(CustomPopupPlacement p)
    {
        if (p.Target is not Visual v)
            return;
        var top = v.GetVisualRoot() as TopLevel;
        if (top == null)
            return;
        var m = v.TransformToVisual(top);
        if (m == null)
            return;
        p.AnchorRectangle = _inlineFlyoutAnchorLocal.TransformToAABB(m.Value);
        // Anchor = bottom of equation rect; Gravity.Bottom = open *below* that edge (Top would open above).
        p.Anchor = PopupAnchor.Bottom;
        p.Gravity = PopupGravity.Bottom;
        p.Offset = new Point(0, 8);
        p.ConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY
            | PopupPositionerConstraintAdjustment.SlideY;
    }

    private void RenderInlineEquations(DrawingContext context)
    {
        if (_inlineEquations.Count == 0 || _textLayout == null) return;

        var foreground = Foreground ?? GetThemeForeground();
        var textBrush = foreground ?? Brushes.Black;
        var activeHl = GetEquationActiveHighlightBrush();

        foreach (var eq in _inlineEquations)
        {
            if (eq.Layout == null) continue;

            try
            {
                var showSource = ShowInlineEquationSourceOnHover
                    && _hoveredInlineEquationCharIndex.HasValue
                    && _hoveredInlineEquationCharIndex.Value == eq.CharIndex;
                if (_inlineEqPreviewActive && eq.CharIndex == _inlineEqCharIndex &&
                    TryGetInlineEquationBounds(eq, pad: 2, out var hl))
                {
                    context.DrawRectangle(activeHl, null, new RoundedRect(hl, 4, 4));
                }

                if (showSource)
                {
                    DrawInlineEquationSourceText(context, eq, textBrush);
                    continue;
                }

                var layoutEq = LogicalCaretToLayoutBoundary(eq.CharIndex);
                var charRect = _textLayout.HitTestTextPosition(layoutEq);
                var x = charRect.X;
                var baselineY = GetTextBaselineYForLayoutIndex(_textLayout, layoutEq);

                var renderContext = new MathRenderContext(context, textBrush, _mathTextCache);
                eq.Layout.Render(renderContext, x, baselineY);
            }
            catch
            {
                // Hit test may fail for out-of-bounds positions
            }
        }
    }

    private void DrawInlineEquationSourceText(DrawingContext context, InlineEquationEntry eq, IBrush foregroundBrush)
    {
        if (_textLayout == null)
            return;
        if (!TryGetInlineEquationBounds(eq, pad: 2, out var bounds))
            return;

        using var latexLayout = new TextLayout(
            eq.Latex ?? string.Empty,
            new Typeface(MonoFont, FontStyle.Normal, FontWeight.Normal),
            Math.Max(11, FontSize * 0.88),
            foregroundBrush,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            TextTrimming.CharacterEllipsis,
            null,
            FlowDirection.LeftToRight,
            Math.Max(bounds.Width - 4, 24));

        var drawPoint = new Point(bounds.X + 2, bounds.Y + Math.Max(0, (bounds.Height - latexLayout.Height) * 0.5));
        latexLayout.Draw(context, drawPoint);
    }

    /// <summary>Y coordinate of the text line baseline at a layout character index (matches TextLayout / surrounding text).</summary>
    private double GetTextBaselineYForLayoutIndex(TextLayout layout, int layoutCharIndex)
    {
        var lines = layout.TextLines;
        if (lines.Count == 0)
            return 0;

        var trailing = layoutCharIndex >= _layoutTextLength;
        var idx = Math.Clamp(layoutCharIndex, 0, Math.Max(0, _layoutTextLength));
        var lineIndex = layout.GetLineIndexFromCharacterIndex(idx, trailing);
        lineIndex = Math.Clamp(lineIndex, 0, lines.Count - 1);

        double y = 0;
        for (var i = 0; i < lineIndex; i++)
            y += lines[i].Height;

        return y + lines[lineIndex].Baseline;
    }
}
