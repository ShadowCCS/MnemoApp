using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mnemo.Infrastructure.Services.LaTeX;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Controls;

public class LaTeXRenderer : Control
{
    public static readonly StyledProperty<Box?> LayoutProperty =
        AvaloniaProperty.Register<LaTeXRenderer, Box?>(nameof(Layout));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<LaTeXRenderer, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<bool> IsInlineModeProperty =
        AvaloniaProperty.Register<LaTeXRenderer, bool>(nameof(IsInlineMode), false);

    private readonly LRUCache<(string, double, uint), FormattedText> _formattedTextCache = new(500);

    /// <summary>Padding from control edge to content on all sides. Content is laid out so it stays inside (padding, padding) to (width-padding, height-padding).</summary>
    private const double DefaultPadding = 6;
    /// <summary>Extra pixels added to measured size to avoid clipping from rounding or ink overflow.</summary>
    private const double BoundsSafety = 2;
    /// <summary>Safety margin for glyph ink overflow (rendering outside layout bounds).</summary>
    private const double InkOverflowSafety = 4;

    /// <summary>Minimal horizontal padding for inline mode. Keeps the control compact for embedding in text flow.</summary>
    private const double InlinePad = 2;
    /// <summary>Minimal ink overflow safety for inline mode.</summary>
    private const double InlineOverflow = 1;

    public Box? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// When true, adjusts layout for inline embedding (e.g., in TextBlock inlines).
    /// The control's bottom edge will align with the text baseline, and depth extends downward.
    /// </summary>
    public bool IsInlineMode
    {
        get => GetValue(IsInlineModeProperty);
        set => SetValue(IsInlineModeProperty, value);
    }

    static LaTeXRenderer()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<LaTeXRenderer>(HorizontalAlignment.Left);
        AffectsRender<LaTeXRenderer>(LayoutProperty, ForegroundProperty);
        AffectsMeasure<LaTeXRenderer>(LayoutProperty, IsInlineModeProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty)
        {
            _formattedTextCache.Clear();
            UpdateMinimumSize();
        }
        else if (change.Property == ForegroundProperty)
        {
            _formattedTextCache.Clear();
        }
        else if (change.Property == IsInlineModeProperty)
        {
            UpdateMinimumSize();
        }
    }

    private void UpdateMinimumSize()
    {
        if (Layout == null)
        {
            MinWidth = 0;
            MinHeight = 0;
            return;
        }

        if (IsInlineMode)
        {
            // Inline mode: baseline at the very bottom of the control.
            // Only above-baseline height is included; depth (subscripts etc.) overflows below
            // and is visible because ClipToBounds defaults to false.
            // This ensures that when InlineUIContainer uses BaselineAlignment.Baseline
            // (which aligns the control's bottom edge with the text baseline), the math
            // baseline lands exactly on the text baseline.
            var baselineY = InlinePad + Layout.Height;
            var actualBounds = CalculateActualBounds(Layout, InlinePad, baselineY);
            var minX = Math.Min(0, actualBounds.Left - InlineOverflow);
            MinWidth = (actualBounds.Right + InlineOverflow) - minX;
            MinHeight = Layout.Height + InlinePad;
        }
        else
        {
            // Standard mode: centered layout with padding on all sides
            var baselineY = DefaultPadding + Layout.Height;
            var actualBounds = CalculateActualBounds(Layout, DefaultPadding, baselineY);
            var minX = Math.Min(0, actualBounds.Left - InkOverflowSafety);
            var minY = Math.Min(0, actualBounds.Top - InkOverflowSafety);
            MinWidth = (actualBounds.Right + InkOverflowSafety) - minX;
            MinHeight = (actualBounds.Bottom + InkOverflowSafety) - minY;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Application.Current != null)
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current != null)
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        _formattedTextCache.Clear();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _formattedTextCache.Clear();
        InvalidateVisual();
    }

    /// <summary>
    /// Returns the size the control needs to display its content. Use this when embedding
    /// in InlineUIContainer so the host can reserve the correct space (e.g. set Width/Height).
    /// </summary>
    public Size GetDesiredSize()
    {
        if (Layout == null)
            return new Size(0, 0);

        if (IsInlineMode)
        {
            // Inline mode: baseline at the very bottom of the control.
            // Height includes only above-baseline content + minimal padding.
            // Depth content (subscripts etc.) overflows below the control bounds.
            var baselineY = InlinePad + Layout.Height;
            var actualBounds = CalculateActualBounds(Layout, InlinePad, baselineY);
            var minX = Math.Min(0, actualBounds.Left - InlineOverflow);
            var width = Math.Max((actualBounds.Right + InlineOverflow) - minX, MinWidth);
            var height = Math.Max(Layout.Height + InlinePad, MinHeight);

            return new Size(width, height);
        }
        else
        {
            // Standard mode: full padding on all sides
            var baselineY = DefaultPadding + Layout.Height;
            var actualBounds = CalculateActualBounds(Layout, DefaultPadding, baselineY);

            var minX = Math.Min(0, actualBounds.Left - InkOverflowSafety);
            var minY = Math.Min(0, actualBounds.Top - InkOverflowSafety);
            // Use ink bounds only; comparing to Layout.Width + padding inflated width past glyphs when the two diverged.
            var maxX = actualBounds.Right + InkOverflowSafety;
            var maxY = actualBounds.Bottom + InkOverflowSafety;

            var width = Math.Max(maxX - minX, MinWidth);
            var height = Math.Max(maxY - minY, MinHeight);

            return new Size(width, height);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredSize = GetDesiredSize();
        
        // If explicit Width/Height are set (e.g., when embedded in InlineUIContainer),
        // respect those but ensure we don't exceed our natural size
        var finalWidth = !double.IsNaN(Width) ? Width : desiredSize.Width;
        var finalHeight = !double.IsNaN(Height) ? Height : desiredSize.Height;
        
        return new Size(
            Math.Min(finalWidth, availableSize.Width),
            Math.Min(finalHeight, availableSize.Height));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Layout == null)
            return;

        double baselineY, offsetX, offsetY, x;

        if (IsInlineMode)
        {
            // Inline mode: baseline is at the very bottom of the control.
            // When the host InlineUIContainer uses BaselineAlignment.Baseline it aligns
            // the control's bottom edge with the text baseline, so placing the math
            // baseline there gives correct vertical alignment with surrounding text.
            // Depth content (subscripts) renders below the control bounds (ClipToBounds
            // is false, so it remains visible).
            baselineY = Bounds.Height;
            var actualBounds = CalculateActualBounds(Layout, InlinePad, baselineY);
            var minX = Math.Min(0, actualBounds.Left - InlineOverflow);
            offsetX = -minX;
            offsetY = 0;
            x = InlinePad;
        }
        else
        {
            // Standard mode: baseline positioned with padding from top
            baselineY = DefaultPadding + Layout.Height;
            var actualBounds = CalculateActualBounds(Layout, DefaultPadding, baselineY);
            var minX = Math.Min(0, actualBounds.Left - InkOverflowSafety);
            var minY = Math.Min(0, actualBounds.Top - InkOverflowSafety);
            offsetX = -minX;
            offsetY = -minY;
            x = DefaultPadding;
        }

        using (context.PushTransform(Matrix.CreateTranslation(offsetX, offsetY)))
        {
            var renderContext = new MathRenderContext(context, GetTextBrush(), _formattedTextCache);
            Layout.Render(renderContext, x, baselineY);
        }
    }

    private IBrush GetTextBrush()
    {
        if (Foreground != null)
            return Foreground;
        if (this.TryFindResource("TextPrimaryBrush", out var resource) && resource is IBrush brush)
            return brush;
        return Brushes.Black;
    }

    /// <summary>Recursively walks the box tree and returns the union of all layout bounds (including glyphs and child boxes) in control coordinates.</summary>
    private static Rect CalculateActualBounds(Box box, double x, double baselineY)
    {
        var top = baselineY - box.Height;
        var left = x;
        var width = box.Width;
        var height = box.TotalHeight;
        var bounds = new Rect(left, top, width, height);

        foreach (var (child, cx, cy) in box.GetChildPositions(x, baselineY))
            bounds = bounds.Union(CalculateActualBounds(child, cx, cy));

        return bounds;
    }
}
