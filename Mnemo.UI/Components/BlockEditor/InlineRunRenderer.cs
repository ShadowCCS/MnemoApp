using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>
/// Builds Avalonia TextBlock.Inlines from a list of InlineRuns.
/// </summary>
public static class InlineRunRenderer
{
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Courier New, monospace");

    public static void Render(TextBlock textBlock, IReadOnlyList<InlineRun> runs)
    {
        textBlock.Inlines?.Clear();
        if (textBlock.Inlines == null)
            textBlock.Inlines = new InlineCollection();

        if (runs.Count == 0)
        {
            textBlock.Inlines.Add(new Run(string.Empty));
            return;
        }

        foreach (var inlineRun in runs)
        {
            var run = new Run(inlineRun.Text);
            ApplyStyle(run, inlineRun.Style);
            textBlock.Inlines.Add(run);
        }
    }

    private static void ApplyStyle(Run run, InlineStyle style)
    {
        if (style.Bold)
            run.FontWeight = FontWeight.Bold;

        if (style.Italic)
            run.FontStyle = FontStyle.Italic;

        if (style.Code)
            run.FontFamily = MonoFont;

        var decorations = new TextDecorationCollection();
        if (style.Underline)
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (style.Strikethrough)
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        if (decorations.Count > 0)
            run.TextDecorations = decorations;

        if (!string.IsNullOrEmpty(style.BackgroundColor))
        {
            if (Color.TryParse(style.BackgroundColor, out var color))
            {
                run.Background = new SolidColorBrush(color);
            }
            else if (style.BackgroundColor.StartsWith("swatch", System.StringComparison.OrdinalIgnoreCase) && Application.Current != null)
            {
                var key = "ColorSwatch" + style.BackgroundColor.Substring(6);
                if (Application.Current.TryFindResource(key, out var res) && res is Color swatchColor)
                {
                    run.Background = new SolidColorBrush(swatchColor);
                }
            }
        }
    }
}
