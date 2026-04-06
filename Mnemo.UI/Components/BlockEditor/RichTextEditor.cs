using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia.Threading;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>
/// Custom editable control that renders styled <see cref="InlineRun"/> lists directly via
/// <see cref="TextLayout"/>, giving pixel-accurate caret and selection geometry that stays
/// aligned with the visible rich text at all times.
/// </summary>
public class RichTextEditor : Control
{
    // ── Avalonia properties ──────────────────────────────────────────────────

    public static readonly StyledProperty<IReadOnlyList<InlineRun>> RunsProperty =
        AvaloniaProperty.Register<RichTextEditor, IReadOnlyList<InlineRun>>(
            nameof(Runs), defaultValue: Array.Empty<InlineRun>());

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
    private TextLayout? _watermarkLayout;
    /// <summary>Text we built the current _textLayout for; used to detect stale layout when Runs were set after first paint.</summary>
    private string? _lastBuiltText;
    /// <summary>Width we last built layout for; used in Render to avoid building with Bounds (can be 0 or stale) and to prevent layout loops.</summary>
    private double _lastLayoutWidth;
    private bool _caretVisible = true;
    private DispatcherTimer? _caretTimer;
    private bool _isDragging;
    private int _dragAnchor;

    private static readonly FontFamily MonoFont =
        new("Cascadia Code, Consolas, Courier New, monospace");

    // ── Routed events ────────────────────────────────────────────────────────

    public static readonly RoutedEvent<TextChangedEventArgs> TextChangedEvent =
        RoutedEvent.Register<RichTextEditor, TextChangedEventArgs>(
            nameof(TextChanged), RoutingStrategies.Bubble);

    public event EventHandler<TextChangedEventArgs>? TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public IReadOnlyList<InlineRun> Runs
    {
        get => GetValue(RunsProperty);
        set => SetValue(RunsProperty, value);
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
    public string Text => FlattenRuns(Runs ?? Array.Empty<InlineRun>());

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
        MinWidthProperty.OverrideDefaultValue<RichTextEditor>(2.0);
        RunsProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.OnRunsChanged());
        FontSizeProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateLayout());
        FontWeightProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateLayout());
        ForegroundProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateLayout());
        WatermarkProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateLayout());
        ShowInactiveWatermarkProperty.Changed.AddClassHandler<RichTextEditor>((o, _) => o.InvalidateVisual());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartCaretTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopCaretTimer();
        DisposeLayouts();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private const double MinLayoutWidth = 200;
    /// <summary>Max width when measure is unconstrained; avoids infinite desired size and layout loops.</summary>
    private const double MaxLayoutWidth = 4096;

    protected override Size MeasureOverride(Size availableSize)
    {
        // Never use infinite width: causes huge desired size and can trigger infinite layout loop.
        var maxWidth = availableSize.Width > 0 && !double.IsInfinity(availableSize.Width)
            ? availableSize.Width : MaxLayoutWidth;
        BuildLayout(maxWidth);
        var height = _textLayout?.Height ?? FontSize;
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? Math.Max(MinLayoutWidth, Math.Min(MaxLayoutWidth, _textLayout?.Width ?? MinLayoutWidth))
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

        var runs = Runs ?? Array.Empty<InlineRun>();
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
            return;
        }

        // Ensure non-null opaque foreground so glyphs are always drawn.
        var safeForeground = foreground ?? new SolidColorBrush(Colors.Black);
        if (safeForeground.Opacity == 0)
            safeForeground = new SolidColorBrush(Colors.Black);

        // Single layout with style overrides so line breaking uses correct metrics (bold vs normal, etc.) and drawing is correct.
        var styleSpans = BuildStyleSpans(runs, safeForeground);
        _textLayout = new TextLayout(
            text, typeface, FontSize, safeForeground,
            TextAlignment.Left, TextWrapping.Wrap, TextTrimming.None,
            null, FlowDirection.LeftToRight, maxWidth,
            double.PositiveInfinity, double.NaN, 0, 0,
            styleSpans.Count > 0 ? styleSpans : null);
        _lastBuiltText = text;
    }

    private List<ValueSpan<TextRunProperties>> BuildStyleSpans(
        IReadOnlyList<InlineRun> runs, IBrush defaultForeground)
    {
        var spans = new List<ValueSpan<TextRunProperties>>(runs.Count);
        int offset = 0;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;

            var style = run.Style;
            var ff = style.Code ? MonoFont : FontFamily.Default;
            var fw = style.Bold ? FontWeight.Bold : FontWeight.Normal;
            var fs = style.Italic ? FontStyle.Italic : FontStyle.Normal;
            var typeface = new Typeface(ff, fs, fw);

            TextDecorationCollection? decorations = null;
            if (style.Underline || style.Strikethrough)
            {
                decorations = new TextDecorationCollection();
                if (style.Underline)
                    decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
                if (style.Strikethrough)
                    decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            }

            IBrush? background = null;
            if (!string.IsNullOrEmpty(style.BackgroundColor))
            {
                if (Color.TryParse(style.BackgroundColor, out var bgColor))
                {
                    background = new SolidColorBrush(bgColor);
                }
                else if (style.BackgroundColor.StartsWith("swatch", StringComparison.OrdinalIgnoreCase) && Application.Current != null)
                {
                    var key = "ColorSwatch" + style.BackgroundColor.Substring(6);
                    if (Application.Current.TryFindResource(key, out var res) && res is Color rc)
                    {
                        background = new SolidColorBrush(rc);
                    }
                }
            }

            var props = new GenericTextRunProperties(
                typeface,
                fontRenderingEmSize: FontSize,
                textDecorations: decorations,
                foregroundBrush: defaultForeground,
                backgroundBrush: background);

            spans.Add(new ValueSpan<TextRunProperties>(offset, run.Text.Length, props));
            offset += run.Text.Length;
        }
        return spans;
    }

    private void OnRunsChanged()
    {
        InvalidateLayout();
        var len = TextLength;
        if (_caretIndex > len) CaretIndex = len;
        int selMax = SelectionIndexUpperBound;
        if (_selectionStart > selMax) SelectionStart = selMax;
        if (_selectionEnd > selMax) SelectionEnd = selMax;
        RaiseEvent(new TextChangedEventArgs(TextChangedEvent));
    }

    private void InvalidateLayout()
    {
        DisposeLayouts();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void DisposeLayouts()
    {
        _textLayout?.Dispose();
        _textLayout = null;
        _watermarkLayout?.Dispose();
        _watermarkLayout = null;
        _lastBuiltText = null;
        _lastLayoutWidth = 0;
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

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        // Rebuild layout only if null or content changed. Use _lastLayoutWidth to avoid building with Bounds (can be 0 during first frame) which can cause layout loops.
        var currentText = Text;
        var layoutWidth = _lastLayoutWidth > 0 ? _lastLayoutWidth : (Bounds.Width > 0 ? Bounds.Width : MinLayoutWidth);
        if (_textLayout == null || currentText != _lastBuiltText)
            BuildLayout(layoutWidth);

        var origin = new Point(0, 0);

        // Selection background
        int selStart = Math.Min(_selectionStart, _selectionEnd);
        int selEnd = Math.Max(_selectionStart, _selectionEnd);
        if (selEnd > selStart && _textLayout != null)
        {
            var selBrush = SelectionBrush ?? new SolidColorBrush(Colors.CornflowerBlue, 0.4);
            if (string.IsNullOrEmpty(Text))
            {
                double h = _textLayout.Height > 0 ? _textLayout.Height : FontSize;
                double w = Math.Max(3.0, FontSize * 0.45);
                context.FillRectangle(selBrush, new Rect(0, 0, w, h));
            }
            else
            {
                var rects = _textLayout.HitTestTextRange(selStart, selEnd - selStart).ToList();
                bool hasDrawable = rects.Any(r => r.Width > 0.5 && r.Height > 0.5);
                if (!hasDrawable)
                {
                    // U+200B and other zero-advance glyphs often yield no range rects; draw a caret-sized chip.
                    try
                    {
                        int idx = TextLength > 0 ? Math.Clamp(selStart, 0, TextLength - 1) : 0;
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

        // Always draw the text layout first so removing the watermark repaints the line (avoids stale glyphs).
        if (_textLayout != null)
        {
            _textLayout.Draw(context, origin);
            // Do not trust VM-only focus: several blocks can briefly (or stuck) have IsFocused while only
            // one RichTextEditor has keyboard focus. Image captions: parent toggles Watermark while hovering the image.
            if (ShouldDrawWatermark() && _watermarkLayout != null)
                _watermarkLayout.Draw(context, origin);
        }

        // Caret
        if (IsFocused && _caretVisible && selEnd == selStart && _textLayout != null)
        {
            var caretBrush = CaretBrush ?? Brushes.Black;
            var caretRect = GetCaretRect();
            context.FillRectangle(caretBrush, caretRect);
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
                return new Rect(charRect.X, charRect.Y, w, h);
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
                return new Rect(charRect.X, charRect.Y, w, h);
            }
            catch
            {
                // fall through
            }
        }

        return new Rect(0, 0, Bounds.Width > 0 ? Bounds.Width : layoutWidth, FontSize);
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
            return new Rect(0, 0, w, h);
        }
        try
        {
            var rects = _textLayout.HitTestTextRange(selStart, selEnd - selStart).ToList();
            bool hasDrawable = rects.Any(r => r.Width > 0.5 && r.Height > 0.5);
            if (hasDrawable)
            {
                var r = rects[0];
                for (int i = 1; i < rects.Count; i++)
                    r = r.Union(rects[i]);
                return r;
            }
            int idx = TextLength > 0 ? Math.Clamp(selStart, 0, TextLength - 1) : 0;
            var pos = _textLayout.HitTestTextPosition(idx);
            double h = pos.Height > 0 ? pos.Height : (_textLayout.Height > 0 ? _textLayout.Height : FontSize);
            double chipW = pos.Width > 0.5 ? pos.Width : Math.Max(3.0, FontSize * 0.45);
            return new Rect(pos.X, pos.Y, chipW, h);
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
            var charRect = _textLayout.HitTestTextPosition(_caretIndex);
            return new Rect(charRect.X, charRect.Y, 1.5, charRect.Height > 0 ? charRect.Height : FontSize);
        }
        catch
        {
            return new Rect(0, 0, 1.5, FontSize);
        }
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
        if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Watermark))
            InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        Focus();
        var pos = e.GetPosition(this);
        var idx = HitTestPoint(pos);

        if (e.ClickCount == 1)
        {
            CaretIndex = idx;
            SelectionStart = idx;
            SelectionEnd = idx;
            _dragAnchor = idx;
            _isDragging = true;
            e.Pointer.Capture(this);
        }
        else if (e.ClickCount == 2)
        {
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
        if (!_isDragging) return;
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
                HandleDelete();
                e.Handled = true;
                break;

            case Key.Back:
                // Let parent tunnel handler deal with merge/block-delete; only handle
                // in-block backspace (caret not at 0, or active selection).
                if (_caretIndex > 0 || HasSelection)
                {
                    if (ctrl && !HasSelection)
                        HandleBackspaceWord();
                    else
                        HandleBackspace();
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
        if (string.IsNullOrEmpty(e.Text)) return;

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
        var flat = FlattenRuns(Runs ?? Array.Empty<InlineRun>());
        if (flat.Length == 0 && start == 0 && end == 1)
        {
            SelectionStart = 0;
            SelectionEnd = 0;
            CaretIndex = 0;
            return;
        }
        var runs = ApplyTextDeletion(Runs ?? Array.Empty<InlineRun>(), start, end);
        Runs = runs;
    }

    private void InsertText(string text)
    {
        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);
        if (TextLength == 0 && start == 0 && end == 1)
            end = 0;

        var runs = ApplyTextInsertion(Runs ?? Array.Empty<InlineRun>(), start, end, text);
        int newCaret = start + text.Length;
        Runs = runs;
        CaretIndex = newCaret;
        SelectionStart = newCaret;
        SelectionEnd = newCaret;
    }

    /// <summary>Inserts text at the caret (replacing selection), same as typed input.</summary>
    public void InsertTextAtCaret(string text) => InsertText(text);

    // ── Run mutation helpers ─────────────────────────────────────────────────

    private static IReadOnlyList<InlineRun> ApplyTextDeletion(
        IReadOnlyList<InlineRun> runs, int start, int end)
    {
        return Core.Formatting.InlineRunFormatApplier.ApplyTextEdit(
            runs, FlattenRuns(runs), FlattenRuns(runs).Remove(start, end - start));
    }

    private static IReadOnlyList<InlineRun> ApplyTextInsertion(
        IReadOnlyList<InlineRun> runs, int selStart, int selEnd, string text)
    {
        var flat = FlattenRuns(runs);
        int removeLen = selEnd - selStart;
        var newFlat = removeLen > 0
            ? flat.Remove(selStart, removeLen).Insert(selStart, text)
            : flat.Insert(selStart, text);
        return Core.Formatting.InlineRunFormatApplier.ApplyTextEdit(runs, flat, newFlat);
    }

    private static string FlattenRuns(IReadOnlyList<InlineRun> runs)
    {
        if (runs.Count == 0) return string.Empty;
        if (runs.Count == 1) return runs[0].Text;
        var sb = new StringBuilder();
        foreach (var r in runs) sb.Append(r.Text);
        return sb.ToString();
    }

    // ── Hit-testing ──────────────────────────────────────────────────────────

    public int HitTestPoint(Point point)
    {
        if (_textLayout == null) return 0;
        try
        {
            var result = _textLayout.HitTestPoint(point);
            int pos = result.TextPosition;
            if (result.IsTrailing && pos < TextLength) pos++;
            return Math.Clamp(pos, 0, TextLength);
        }
        catch
        {
            return 0;
        }
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

    private void EnsureLayoutForVerticalNavigation()
    {
        var w = Bounds.Width > 0 && !double.IsNaN(Bounds.Width)
            ? Bounds.Width
            : (_lastLayoutWidth > 0 ? _lastLayoutWidth : MinLayoutWidth);
        var needRebuild = _textLayout == null || Text != _lastBuiltText;
        if (!needRebuild && Bounds.Width > 0 && _textLayout != null
            && Math.Abs(Bounds.Width - _textLayout.MaxWidth) > 0.5)
            needRebuild = true;
        if (needRebuild)
            BuildLayout(w);
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
        var idxForLineLookup = Math.Clamp(_caretIndex, 0, TextLength);
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

        var hitPos = Math.Clamp(_caretIndex, 0, TextLength);
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

        var newLine = layout.GetLineIndexFromCharacterIndex(
            Math.Clamp(newCaretIndex, 0, TextLength),
            newCaretIndex >= TextLength);

        if (newLine != targetVisualLine)
        {
            var col = Math.Max(0, _caretIndex - lines[oldLine].FirstTextSourceIndex);
            newCaretIndex = FallbackCaretSameColumn(lines[targetVisualLine], col);
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
            var pos = result.TextPosition;
            if (result.IsTrailing && pos < TextLength)
                pos++;
            return Math.Clamp(pos, 0, TextLength);
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

    private int FindWordStart(int pos)
    {
        var text = Text;
        pos = Math.Clamp(pos, 0, text.Length);
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1])) pos--;
        return pos;
    }

    private int FindWordEnd(int pos)
    {
        var text = Text;
        pos = Math.Clamp(pos, 0, text.Length);
        while (pos < text.Length && !char.IsWhiteSpace(text[pos])) pos++;
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
}
