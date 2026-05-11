using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Mnemo.UI.Controls;

public sealed class TextSelectionBackgroundLayer : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<int> SelectionStartProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, int>(nameof(SelectionStart));

    public static readonly StyledProperty<int> SelectionEndProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, int>(nameof(SelectionEnd));

    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, IBrush?>(nameof(SelectionBrush));

    public static readonly StyledProperty<Thickness> TextPaddingProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, Thickness>(nameof(TextPadding));

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, TextWrapping>(nameof(TextWrapping));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, double>(nameof(FontSize), 13);

    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, double>(nameof(LineHeight), double.NaN);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<TextSelectionBackgroundLayer, FontFamily>(
            nameof(FontFamily),
            new FontFamily("Cascadia Code,Consolas,Courier New,monospace"));

    static TextSelectionBackgroundLayer()
    {
        AffectsRender<TextSelectionBackgroundLayer>(
            TextProperty,
            SelectionStartProperty,
            SelectionEndProperty,
            SelectionBrushProperty,
            TextPaddingProperty,
            TextWrappingProperty,
            FontSizeProperty,
            LineHeightProperty,
            FontFamilyProperty);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public int SelectionStart
    {
        get => GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public int SelectionEnd
    {
        get => GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public Thickness TextPadding
    {
        get => GetValue(TextPaddingProperty);
        set => SetValue(TextPaddingProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var text = Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), 0, text.Length);
        if (end <= start || string.IsNullOrEmpty(text))
            return;

        var brush = SelectionBrush ?? TryThemeSelectionBrush() ?? Brushes.LightBlue;
        var padding = TextPadding;
        var maxWidth = TextWrapping == TextWrapping.NoWrap
            ? double.PositiveInfinity
            : Math.Max(1, Bounds.Width - padding.Left - padding.Right);

        using var layout = new TextLayout(
            text,
            new Typeface(FontFamily),
            FontSize,
            Brushes.Transparent,
            TextAlignment.Left,
            TextWrapping,
            TextTrimming.None,
            null,
            FlowDirection.LeftToRight,
            maxWidth,
            double.PositiveInfinity,
            LineHeight,
            0,
            0,
            null,
            null);

        var rects = layout.HitTestTextRange(start, end - start)
            .Where(rect => rect.Width > 0.5 && rect.Height > 0.5);

        var offset = new Vector(padding.Left, padding.Top);
        foreach (var rect in rects)
            context.FillRectangle(brush, rect.Translate(offset));
    }

    private static IBrush? TryThemeSelectionBrush()
    {
        var app = Application.Current;
        return app != null
               && app.TryGetResource("TextControlSelectionHighlightColorBrush", app.ActualThemeVariant, out var resource)
               && resource is IBrush brush
            ? brush
            : null;
    }
}
