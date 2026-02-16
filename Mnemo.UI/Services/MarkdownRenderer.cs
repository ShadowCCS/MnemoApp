using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mnemo.Core.Models.Markdown;
using Mnemo.Core.Services;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;

namespace Mnemo.UI.Services;

public class MarkdownRenderer : IMarkdownRenderer
{
    private readonly ILaTeXEngine _latexEngine;
    private readonly ISettingsService _settingsService;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public MarkdownRenderer(ILaTeXEngine latexEngine, ISettingsService settingsService)
    {
        _latexEngine = latexEngine;
        _settingsService = settingsService;
    }

    public async Task<Control> RenderAsync(string markdown, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground = null)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var spacing = await GetBlockSpacingAsync();
        var container = new StackPanel { Spacing = spacing };

        foreach (var block in document)
        {
            var rendered = await RenderBlockAsync(block, specialInlines, foreground);
            if (rendered != null)
                container.Children.Add(rendered);
        }

        return container;
    }

    private async Task<double> GetBlockSpacingAsync()
    {
        var val = await _settingsService.GetAsync("Markdown.BlockSpacing", "Normal");
        return val switch
        {
            "Compact" => 6.0,
            "Relaxed" => 20.0,
            _ => 12.0
        };
    }

    private async Task<double> GetBaseFontSizeAsync()
    {
        var val = await _settingsService.GetAsync("Markdown.FontSize", "14px");
        if (double.TryParse(val.Replace("px", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
        return 14.0;
    }

    private async Task<double> GetCodeFontSizeAsync()
    {
        var val = await _settingsService.GetAsync("Markdown.CodeFontSize", "14px");
        if (double.TryParse(val.Replace("px", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
        return 14.0;
    }

    private async Task<double> GetMathFontSizeAsync(bool isDisplay)
    {
        var val = await _settingsService.GetAsync("Markdown.MathFontSize", "16px");
        if (double.TryParse(val.Replace("px", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)) 
            return isDisplay ? result + 2 : result;
        return isDisplay ? 18.0 : 16.0;
    }

    private async Task<double> GetLineHeightAsync()
    {
        var val = await _settingsService.GetAsync("Markdown.LineHeight", "1.2");
        if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
        return 1.2;
    }

    private async Task<double> GetLetterSpacingAsync()
    {
        var val = await _settingsService.GetAsync("Markdown.LetterSpacing", "0");
        if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
        return 0.0;
    }

    private async Task<Control?> RenderBlockAsync(Markdig.Syntax.Block block, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground, int listDepth = 0)
    {
        return block switch
        {
            ParagraphBlock paragraph => await RenderParagraphAsync(paragraph, specialInlines, foreground),
            HeadingBlock heading => await RenderHeadingAsync(heading, specialInlines, foreground),
            CodeBlock code => await RenderCodeBlockAsync(code),
            QuoteBlock quote => await RenderQuoteAsync(quote, specialInlines, foreground),
            ListBlock list => await RenderListAsync(list, specialInlines, foreground, listDepth),
            var table when table.GetType().Name == "Table" => await RenderTableAsync(table, specialInlines, foreground),
            ThematicBreakBlock => new Border
            {
                Height = 1,
                Margin = new Thickness(0, 8),
                Background = (SolidColorBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!
            },
            _ => null
        };
    }

    private async Task<Control> RenderParagraphAsync(ParagraphBlock paragraph, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground)
    {
        var fontSize = await GetBaseFontSizeAsync();
        var lineHeight = await GetLineHeightAsync();
        var letterSpacing = await GetLetterSpacingAsync();

        // Check whether this paragraph contains any display math.
        // If it does, we render the paragraph as a StackPanel with text segments
        // separated by standalone (block-level) display math controls, instead of
        // forcing large formulas into InlineUIContainer which garbles layout.
        if (ParagraphContainsDisplayMath(paragraph, specialInlines))
        {
            return await RenderParagraphWithDisplayMathAsync(paragraph, specialInlines, foreground, fontSize, lineHeight, letterSpacing);
        }

        // Simple path: no display math — single TextBlock with inline content.
        var lineHeightPx = fontSize * lineHeight;
        // Inflate LineHeight to fit inline math so Avalonia allocates enough vertical space
        // in the line box (Avalonia caps line boxes at LineHeight, unlike WPF).
        var effectiveLineHeightPx = await GetEffectiveLineHeightAsync(paragraph, specialInlines, lineHeightPx);
        var textBlock = CreateParagraphTextBlock(fontSize, lineHeight, letterSpacing, foreground);
        textBlock.LineHeight = effectiveLineHeightPx;

        if (paragraph.Inline != null && textBlock.Inlines != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                await RenderInlineToInlinesAsync(inline, textBlock.Inlines, specialInlines, foreground, contextFontSize: fontSize, contextLineHeightPx: effectiveLineHeightPx);
            }
        }

        return textBlock;
    }

    /// <summary>Returns true if any literal inline in this paragraph references a display-math placeholder.</summary>
    private static bool ParagraphContainsDisplayMath(ParagraphBlock paragraph, Dictionary<string, MarkdownSpecialInline> specialInlines)
    {
        if (paragraph.Inline == null) return false;

        foreach (var inline in paragraph.Inline)
        {
            if (inline is not LiteralInline literal) continue;
            var text = literal.Content.ToString();
            foreach (var kvp in specialInlines)
            {
                if (kvp.Value.Type == MarkdownInlineType.DisplayMath && text.Contains(kvp.Key))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Renders a paragraph that contains display math. The paragraph is split at each
    /// display-math placeholder: surrounding text is rendered in TextBlocks, and each
    /// display-math formula is rendered as a standalone centred LaTeX control.
    /// </summary>
    private async Task<Control> RenderParagraphWithDisplayMathAsync(
        ParagraphBlock paragraph,
        Dictionary<string, MarkdownSpecialInline> specialInlines,
        IBrush? foreground,
        double fontSize,
        double lineHeight,
        double letterSpacing)
    {
        var container = new StackPanel { Spacing = 6 };
        var currentTextBlock = CreateParagraphTextBlock(fontSize, lineHeight, letterSpacing, foreground);

        if (paragraph.Inline != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                if (inline is LiteralInline literal)
                {
                    // Process the literal text, splitting at display-math placeholders
                    currentTextBlock = await ProcessLiteralWithDisplayMathAsync(
                        literal.Content.ToString(),
                        currentTextBlock,
                        container,
                        specialInlines,
                        foreground,
                        fontSize, lineHeight, letterSpacing);
                }
                else
                {
                    // Non-literal inlines go straight into the current TextBlock
                    var lineHeightPx = fontSize * lineHeight;
                    if (currentTextBlock.Inlines != null)
                        await RenderInlineToInlinesAsync(inline, currentTextBlock.Inlines, specialInlines, foreground, contextFontSize: fontSize, contextLineHeightPx: lineHeightPx);
                }
            }
        }

        // Flush remaining text
        if (currentTextBlock.Inlines?.Count > 0)
            container.Children.Add(currentTextBlock);

        if (container.Children.Count > 1)
            container.ClipToBounds = false;

        return container.Children.Count == 1 ? (Control)container.Children[0] : container;
    }

    /// <summary>
    /// Walks a literal text segment, emitting runs / inline-math into the current TextBlock
    /// and breaking out display-math formulas as standalone controls in the container.
    /// Returns the (possibly new) current TextBlock to continue appending into.
    /// </summary>
    private async Task<TextBlock> ProcessLiteralWithDisplayMathAsync(
        string text,
        TextBlock currentTextBlock,
        StackPanel container,
        Dictionary<string, MarkdownSpecialInline> specialInlines,
        IBrush? foreground,
        double fontSize, double lineHeight, double letterSpacing)
    {
        if (string.IsNullOrEmpty(text))
            return currentTextBlock;

        var position = 0;

        while (position < text.Length)
        {
            // Find the next placeholder marker
            var markerIndex = text.IndexOf("Ⓢ", position, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                // No more markers — append remainder as inline text
                AppendRunIfNonEmpty(currentTextBlock, text.Substring(position), foreground);
                break;
            }

            var endMarkerIndex = text.IndexOf("Ⓢ", markerIndex + 1, StringComparison.Ordinal);
            if (endMarkerIndex < 0)
            {
                AppendRunIfNonEmpty(currentTextBlock, text.Substring(position), foreground);
                break;
            }

            var potentialKey = text.Substring(markerIndex, endMarkerIndex - markerIndex + 1);

            if (specialInlines.TryGetValue(potentialKey, out var inlineData))
            {
                // Emit any text before the marker into the current TextBlock
                if (markerIndex > position)
                    AppendRunIfNonEmpty(currentTextBlock, text.Substring(position, markerIndex - position), foreground);

                if (inlineData.Type == MarkdownInlineType.DisplayMath)
                {
                    // ---- Display math: render as a standalone block ----
                    // Flush the current TextBlock into the container
                    if (currentTextBlock.Inlines?.Count > 0)
                    {
                        container.Children.Add(currentTextBlock);
                        currentTextBlock = CreateParagraphTextBlock(fontSize, lineHeight, letterSpacing, foreground);
                    }

                    if (await _settingsService.GetAsync("Markdown.RenderMath", true) && !string.IsNullOrWhiteSpace(inlineData.Content))
                    {
                        var displayFontSize = await GetMathFontSizeAsync(true);
                        if (await _latexEngine.BuildLayoutAsync(inlineData.Content.Trim(), displayFontSize) is Mnemo.UI.Controls.LaTeXRenderer displayMathControl)
                        {
                            ApplyInlineMathLayout(displayMathControl, foreground, isInline: false);
                            // Wrap in a centred border so it sits on its own line
                            var wrapper = new Border
                            {
                                Child = displayMathControl,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Margin = new Thickness(0, 4)
                            };
                            container.Children.Add(wrapper);
                        }
                    }
                    else
                    {
                        container.Children.Add(new TextBlock
                        {
                            Text = $"$${inlineData.Content}$$",
                            Foreground = foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!,
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                }
                else
                {
                    // Non-display-math special inline — render into the current TextBlock
                    if (currentTextBlock.Inlines != null)
                        await RenderSpecialInlineAsync(inlineData, currentTextBlock.Inlines, foreground);
                }

                position = endMarkerIndex + 1;
            }
            else
            {
                // Not a recognised placeholder — advance past the first marker
                position = markerIndex + 1;
            }
        }

        return currentTextBlock;
    }

    /// <summary>Renders a single non-display-math special inline into the given InlineCollection.</summary>
    private async Task RenderSpecialInlineAsync(MarkdownSpecialInline inlineData, InlineCollection inlines, IBrush? foreground, bool isHeading = false)
    {
        switch (inlineData.Type)
        {
            case MarkdownInlineType.InlineMath:
                if (await _settingsService.GetAsync("Markdown.RenderMath", true) && !string.IsNullOrWhiteSpace(inlineData.Content))
                {
                    var inlineFontSize = await GetMathFontSizeAsync(false);
                    if (await _latexEngine.BuildLayoutAsync(inlineData.Content.Trim(), inlineFontSize) is Mnemo.UI.Controls.LaTeXRenderer inlineMathControl)
                    {
                        ApplyInlineMathLayout(inlineMathControl, foreground, isInline: true);
                        inlines.Add(new InlineUIContainer { Child = inlineMathControl, BaselineAlignment = BaselineAlignment.Center });
                    }
                }
                else
                {
                    inlines.Add(new Run { Text = $"${inlineData.Content}$", Foreground = (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!) });
                }
                break;

            case MarkdownInlineType.Highlight:
                inlines.Add(new Run
                {
                    Text = inlineData.Content,
                    Background = (IBrush)Application.Current!.FindResource("HighlightedTextBrush")!,
                    Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!))
                });
                break;

            case MarkdownInlineType.Superscript:
                var superscriptTextBlock = new TextBlock
                {
                    Text = inlineData.Content,
                    FontSize = 10,
                    Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!)),
                };
                inlines.Add(new InlineUIContainer { Child = superscriptTextBlock, BaselineAlignment = BaselineAlignment.Superscript });
                break;

            case MarkdownInlineType.Subscript:
                var subscriptTextBlock = new TextBlock
                {
                    Text = inlineData.Content,
                    FontSize = 10,
                    Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!)),
                };
                inlines.Add(new InlineUIContainer { Child = subscriptTextBlock, BaselineAlignment = BaselineAlignment.Subscript });
                break;

            case MarkdownInlineType.Strikethrough:
                inlines.Add(new Run
                {
                    Text = inlineData.Content,
                    TextDecorations = TextDecorations.Strikethrough,
                    Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!))
                });
                break;
        }
    }

    private static void AppendRunIfNonEmpty(TextBlock textBlock, string text, IBrush? foreground)
    {
        if (string.IsNullOrWhiteSpace(text) || textBlock.Inlines == null) return;
        textBlock.Inlines.Add(new Run
        {
            Text = text,
            Foreground = foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!
        });
    }

    private static TextBlock CreateParagraphTextBlock(double fontSize, double lineHeight, double letterSpacing, IBrush? foreground)
    {
        return new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            LineHeight = fontSize * lineHeight,
            LetterSpacing = letterSpacing,
            Foreground = foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!,
            ClipToBounds = false
        };
    }

    private async Task RenderInlineToInlinesAsync(Markdig.Syntax.Inlines.Inline inline, InlineCollection inlines, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground, bool isHeading = false, double? contextFontSize = null, double? contextLineHeightPx = null)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                await ReplaceSpecialPlaceholdersAsync(text, inlines, specialInlines, foreground, isHeading);
                break;

            case EmphasisInline emphasis:
                var span = new Span();
                if (emphasis.DelimiterCount == 2)
                    span.FontWeight = FontWeight.Bold;
                else if (emphasis.DelimiterCount == 1)
                    span.FontStyle = FontStyle.Italic;

                foreach (var child in emphasis)
                {
                    await RenderInlineToInlinesAsync(child, span.Inlines, specialInlines, foreground, isHeading, contextFontSize, contextLineHeightPx);
                }
                inlines.Add(span);
                break;

            case CodeInline code:
                inlines.Add(new Run
                {
                    Text = code.Content,
                    FontFamily = new FontFamily("Consolas,monospace"),
                    Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!
                });
                break;

            case LinkInline link:
                var linkButton = new HyperlinkButton
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    MinHeight = 0,
                    MinWidth = 0,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    VerticalContentAlignment = VerticalAlignment.Bottom
                };

                var linkFontSize = contextFontSize ?? await GetBaseFontSizeAsync();
                // Slightly more than font size so link doesn't sit too low; less than full paragraph line height so it doesn't sit above
                var linkLineHeight = linkFontSize * 1.15;
                var linkContent = new TextBlock
                {
                    FontSize = linkFontSize,
                    LineHeight = linkLineHeight,
                    Foreground = (IBrush)Application.Current!.FindResource("LinksBrush")!,
                    TextDecorations = TextDecorations.Underline,
                    Background = Brushes.Transparent
                };

                if (link.FirstChild is LiteralInline linkLiteral)
                {
                    linkContent.Text = linkLiteral.Content.ToString();
                }
                else
                {
                    foreach (var child in link)
                    {
                        if (linkContent.Inlines != null)
                        {
                            await RenderInlineToInlinesAsync(child, linkContent.Inlines, specialInlines, foreground, isHeading, contextFontSize, contextLineHeightPx);
                        }
                    }
                }

                linkButton.Content = linkContent;
                linkButton.Click += (sender, e) =>
                {
                    HandleLinkClick(link.Url);
                    e.Handled = true;
                };

                inlines.Add(new InlineUIContainer
                {
                    Child = linkButton,
                    BaselineAlignment = BaselineAlignment.Center
                });
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    await RenderInlineToInlinesAsync(child, inlines, specialInlines, foreground, isHeading, contextFontSize, contextLineHeightPx);
                }
                break;
        }
    }

    /// <summary>
    /// Applies layout so LaTeXRenderer works correctly inside InlineUIContainer: explicit size
    /// ensures the text layout reserves the right space and avoids overlap/garbled layout.
    /// Forces a layout pass to ensure proper measurement before embedding in inline context.
    /// </summary>
    private static void ApplyInlineMathLayout(Mnemo.UI.Controls.LaTeXRenderer control, IBrush? foreground, bool isInline)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Bottom;
        
        // Enable inline mode for proper baseline alignment
        control.IsInlineMode = isInline;

        // In inline mode, depth (subscripts etc.) renders below the control bounds.
        // Ensure the control doesn't clip that overflow.
        if (isInline)
            control.ClipToBounds = false;
        
        if (foreground != null)
            control.SetValue(Mnemo.UI.Controls.LaTeXRenderer.ForegroundProperty, foreground);
        
        // Force a measure pass with infinite available size to get the natural size
        control.Measure(Size.Infinity);
        var size = control.DesiredSize;
        
        // Set explicit dimensions for InlineUIContainer layout
        control.Width = size.Width;
        control.Height = size.Height;
    }

    private async Task ReplaceSpecialPlaceholdersAsync(string text, InlineCollection inlines, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground, bool isHeading = false)
    {
        if (string.IsNullOrEmpty(text) || specialInlines.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(text))
                inlines.Add(new Run { Text = text, Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!)) });
            return;
        }

        var position = 0;
        var textLength = text.Length;
        
        while (position < textLength)
        {
            var markerIndex = text.IndexOf("Ⓢ", position, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                var remainingText = text.Substring(position);
                if (!string.IsNullOrWhiteSpace(remainingText))
                    inlines.Add(new Run { Text = remainingText, Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!)) });
                break;
            }

            var endMarkerIndex = text.IndexOf("Ⓢ", markerIndex + 1, StringComparison.Ordinal);
            if (endMarkerIndex < 0)
            {
                var remainingText = text.Substring(position);
                if (!string.IsNullOrWhiteSpace(remainingText))
                    inlines.Add(new Run { Text = remainingText, Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!)) });
                break;
            }

            var potentialKey = text.Substring(markerIndex, endMarkerIndex - markerIndex + 1);
            
            if (specialInlines.TryGetValue(potentialKey, out var inlineData))
            {
                if (markerIndex > position)
                {
                    var beforeText = text.Substring(position, markerIndex - position);
                    if (!string.IsNullOrWhiteSpace(beforeText))
                        inlines.Add(new Run { Text = beforeText, Foreground = (isHeading ? (IBrush)Application.Current!.FindResource("TextPrimaryBrush")! : (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!)) });
                }

                if (inlineData.Type == MarkdownInlineType.DisplayMath)
                {
                    // Display math in non-display-math paragraphs (e.g. headings, quotes).
                    // Fall back to inline rendering since we can't split the block here.
                    if (await _settingsService.GetAsync("Markdown.RenderMath", true) && !string.IsNullOrWhiteSpace(inlineData.Content))
                    {
                        var displayFontSize = await GetMathFontSizeAsync(true);
                        if (await _latexEngine.BuildLayoutAsync(inlineData.Content.Trim(), displayFontSize) is Mnemo.UI.Controls.LaTeXRenderer displayMathControl)
                        {
                            ApplyInlineMathLayout(displayMathControl, foreground, isInline: false);
                            inlines.Add(new InlineUIContainer { Child = displayMathControl, BaselineAlignment = BaselineAlignment.Center });
                        }
                    }
                    else
                    {
                        inlines.Add(new Run { Text = $"$${inlineData.Content}$$", Foreground = (foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!) });
                    }
                }
                else
                {
                    // All other special inlines (inline math, highlight, sub/superscript, strikethrough)
                    await RenderSpecialInlineAsync(inlineData, inlines, foreground, isHeading);
                }

                position = endMarkerIndex + 1;
            }
            else
            {
                position = markerIndex + 1;
            }
        }
    }

    private async Task<Control> RenderHeadingAsync(HeadingBlock heading, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground)
    {
        var fontSize = heading.Level switch
        {
            1 => 32,
            2 => 24,
            3 => 20,
            4 => 18,
            5 => 16,
            _ => 14
        };
        var lineHeight = await GetLineHeightAsync();
        var letterSpacing = await GetLetterSpacingAsync();

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Bold,
            FontSize = fontSize,
            LineHeight = fontSize * lineHeight,
            LetterSpacing = letterSpacing,
            Margin = new Thickness(0, heading.Level == 1 ? 16 : 12, 0, 8),
            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
        };

        if (heading.Inline != null && textBlock.Inlines != null)
        {
            var headingLineHeightPx = fontSize * lineHeight;
            foreach (var inline in heading.Inline)
            {
                await RenderInlineToInlinesAsync(inline, textBlock.Inlines, specialInlines, foreground, isHeading: true, contextFontSize: fontSize, contextLineHeightPx: headingLineHeightPx);
            }
        }

        return textBlock;
    }

    private async Task<Control> RenderCodeBlockAsync(CodeBlock codeBlock)
    {
        var fenced = codeBlock as FencedCodeBlock;
        var language = fenced?.Info ?? string.Empty;
        var code = fenced?.Lines.ToString() ?? ((LeafBlock)codeBlock).Lines.ToString();
        var codeFontSize = await GetCodeFontSizeAsync();

        var container = new Border
        {
            Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
            BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 8)
        };

        var stackPanel = new StackPanel();

        if (!string.IsNullOrEmpty(language))
        {
            var headerPanel = new Grid
            {
                Height = 32,
                Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
                Margin = new Thickness(0, 0, 0, 1)
            };

            var languageLabel = new TextBlock
            {
                Text = language.ToUpper(),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                Foreground = (IBrush)Application.Current!.FindResource("TextTertiaryBrush")!,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            var copyButton = new Button
            {
                Content = "Copy",
                FontSize = 11,
                Padding = new Thickness(8, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
                Foreground = (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!,
                BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };

            copyButton.Click += async (sender, e) =>
            {
                try
                {
                    var topLevel = TopLevel.GetTopLevel(copyButton);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(code);
                    }
                }
                catch
                {
                    // Clipboard access might fail
                }
            };

            headerPanel.Children.Add(languageLabel);
            headerPanel.Children.Add(copyButton);
            stackPanel.Children.Add(headerPanel);
        }
        else
        {
            var copyButton = new Button
            {
                Content = "Copy",
                FontSize = 11,
                Padding = new Thickness(8, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 8, 0),
                Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
                Foreground = (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!,
                BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };

            copyButton.Click += async (sender, e) =>
            {
                try
                {
                    var topLevel = TopLevel.GetTopLevel(copyButton);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(code);
                    }
                }
                catch
                {
                    // Clipboard access might fail
                }
            };

            stackPanel.Children.Add(copyButton);
        }

        var codeTextBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("JetBrains Mono, Consolas, 'Courier New', monospace"),
            FontSize = codeFontSize,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!,
            Padding = new Thickness(12),
            Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!
        };

        if (!string.IsNullOrEmpty(language))
        {
            ApplyBasicSyntaxHighlighting(codeTextBlock, code, language);
        }

        stackPanel.Children.Add(codeTextBlock);
        container.Child = stackPanel;
        return container;
    }

    private void ApplyBasicSyntaxHighlighting(TextBlock textBlock, string code, string language)
    {
        var inlines = new InlineCollection();
        var lines = code.Split('\n');
        
        foreach (var line in lines)
        {
            var processedLine = ProcessLineForSyntaxHighlighting(line, language);
            inlines.AddRange(processedLine);
            inlines.Add(new LineBreak());
        }

        if (inlines.Count > 0 && inlines[inlines.Count - 1] is LineBreak)
        {
            inlines.RemoveAt(inlines.Count - 1);
        }

        textBlock.Inlines = inlines;
    }

    private List<Avalonia.Controls.Documents.Inline> ProcessLineForSyntaxHighlighting(string line, string language)
    {
        var inlines = new List<Avalonia.Controls.Documents.Inline>();
        var currentText = line;
        
        var patterns = GetHighlightingPatterns(language);
        
        foreach (var (pattern, brushKey) in patterns)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(currentText, pattern);
            if (matches.Count > 0)
            {
                var lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Index > lastIndex)
                    {
                        inlines.Add(new Run 
                        { 
                            Text = currentText.Substring(lastIndex, match.Index - lastIndex),
                            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
                        });
                    }
                    
                    inlines.Add(new Run 
                    { 
                        Text = match.Value,
                        Foreground = (IBrush)Application.Current!.FindResource(brushKey)!
                    });
                    
                    lastIndex = match.Index + match.Length;
                }
                
                if (lastIndex < currentText.Length)
                {
                    inlines.Add(new Run 
                    { 
                        Text = currentText.Substring(lastIndex),
                        Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
                    });
                }
                return inlines;
            }
        }
        
        inlines.Add(new Run 
        { 
            Text = line,
            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
        });
        
        return inlines;
    }

    private List<(string pattern, string brushKey)> GetHighlightingPatterns(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "cs" => new List<(string, string)>
            {
                (@"\b(using|namespace|class|interface|public|private|protected|static|void|int|string|bool|var|if|else|for|while|foreach|return|new|this|base)\b", "KeywordBrush"),
                (@"//.*$", "CommentBrush"),
                (@"""[^""]*""", "StringBrush"),
                (@"'[^']*'", "StringBrush")
            },
            "javascript" or "js" => new List<(string, string)>
            {
                (@"\b(function|var|let|const|if|else|for|while|return|new|this|class|extends|import|export)\b", "KeywordBrush"),
                (@"//.*$", "CommentBrush"),
                (@"""[^""]*""", "StringBrush"),
                (@"'[^']*'", "StringBrush")
            },
            "python" or "py" => new List<(string, string)>
            {
                (@"\b(def|class|if|else|elif|for|while|return|import|from|as|try|except|finally|with|lambda)\b", "KeywordBrush"),
                (@"#.*$", "CommentBrush"),
                (@"""[^""]*""", "StringBrush"),
                (@"'[^']*'", "StringBrush")
            },
            "html" => new List<(string, string)>
            {
                (@"<[^>]+>", "TagBrush"),
                (@"&[a-zA-Z]+;", "EntityBrush")
            },
            "css" => new List<(string, string)>
            {
                (@"[.#]?[a-zA-Z-]+\s*{", "SelectorBrush"),
                (@"[a-zA-Z-]+\s*:", "PropertyBrush"),
                (@"//.*$", "CommentBrush")
            },
            "json" => new List<(string, string)>
            {
                (@"""[^""]*""(?=\s*:)", "KeywordBrush"), // Keys
                (@"""[^""]*""", "StringBrush"), // Values
                (@"\b(true|false|null)\b", "KeywordBrush"),
                (@"\b[0-9]+\b", "NumberBrush")
            },
            "sql" => new List<(string, string)>
            {
                (@"\b(SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|JOIN|GROUP|ORDER|BY|HAVING|CREATE|TABLE|ALTER|DROP|INTO|VALUES|SET|AND|OR|NOT|NULL|PRIMARY|KEY|FOREIGN|REFERENCES|ON)\b", "KeywordBrush"),
                (@"--.*$", "CommentBrush"),
                (@"""[^""]*""", "StringBrush"),
                (@"'[^']*'", "StringBrush")
            },
            "yaml" or "yml" => new List<(string, string)>
            {
                (@"^\s*[a-zA-Z0-9_-]+\s*:", "KeywordBrush"), // Keys
                (@"#.*$", "CommentBrush"),
                (@"""[^""]*""", "StringBrush"),
                (@"'[^']*'", "StringBrush")
            },
            _ => new List<(string, string)>()
        };
    }

    private async Task<Control> RenderQuoteAsync(QuoteBlock quote, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground)
    {
        var border = new Border
        {
            BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0),
            Margin = new Thickness(0, 4)
        };

        var container = new StackPanel { Spacing = 8 };

        foreach (var block in quote)
        {
            var rendered = await RenderBlockAsync(block, specialInlines, foreground);
            if (rendered != null)
                container.Children.Add(rendered);
        }

        border.Child = container;
        return border;
    }

    private string GetBulletSymbol(int depth, bool isOrdered, int index)
    {
        if (isOrdered)
        {
            return $"{index}.";
        }

        return depth switch
        {
            0 => "•",  // Filled circle
            1 => "○",  // Circle
            2 => "▪",  // Square
            3 => "▫",  // White square
            _ => "•"   // Cycle back to filled circle for deeper nesting
        };
    }

    /// <summary>
    /// Returns the effective line height for a paragraph: the normal line height, or the tallest
    /// inline-math box height if the paragraph contains inline LaTeX. Avalonia caps line boxes at
    /// the TextBlock.LineHeight value, so we must inflate it to prevent math from overflowing.
    /// </summary>
    private async Task<double> GetEffectiveLineHeightAsync(ParagraphBlock paragraph, Dictionary<string, MarkdownSpecialInline> specialInlines, double lineHeightPx)
    {
        if (paragraph.Inline == null || specialInlines.Count == 0)
            return lineHeightPx;

        var inlineFontSize = await GetMathFontSizeAsync(false);
        const double inlinePad = 2;
        var maxLaTeXHeight = 0.0;

        foreach (var kvp in specialInlines)
        {
            if (kvp.Value.Type != MarkdownInlineType.InlineMath || string.IsNullOrWhiteSpace(kvp.Value.Content))
                continue;
            if (!ParagraphContainsPlaceholder(paragraph, kvp.Key))
                continue;

            var boxObj = await _latexEngine.GetLayoutBoxAsync(kvp.Value.Content.Trim(), inlineFontSize);
            if (boxObj is Box box)
                maxLaTeXHeight = Math.Max(maxLaTeXHeight, box.TotalHeight + inlinePad);
        }

        return maxLaTeXHeight > 0 ? Math.Max(lineHeightPx, maxLaTeXHeight) : lineHeightPx;
    }

    private static bool ParagraphContainsPlaceholder(ParagraphBlock paragraph, string placeholder)
    {
        if (paragraph.Inline == null) return false;
        foreach (var inline in paragraph.Inline)
        {
            if (inline is LiteralInline literal && literal.Content.ToString().Contains(placeholder, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private async Task<Control> RenderListAsync(ListBlock list, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground, int depth = 0)
    {
        // Use same spacing as document block spacing so intro line → list matches list item → list item
        var listSpacing = await GetBlockSpacingAsync();
        var container = new StackPanel { Spacing = listSpacing, Margin = new Thickness(0, 0) };
        var fontSize = await GetBaseFontSizeAsync();
        var lineHeight = await GetLineHeightAsync();
        var letterSpacing = await GetLetterSpacingAsync();
        var lineHeightPx = fontSize * lineHeight;
        var bulletLineHeight = lineHeightPx;
        int index = 1;

        foreach (var item in list.Cast<ListItemBlock>())
        {
            var firstBlock = item.Cast<Markdig.Syntax.Block>().FirstOrDefault();
            var firstLineHeight = firstBlock is ParagraphBlock firstParagraph
                ? await GetEffectiveLineHeightAsync(firstParagraph, specialInlines, lineHeightPx)
                : lineHeightPx;

            // When the first line is tall (inline math), offset the bullet so it aligns
            // with the text baseline rather than sitting at the very top of the row.
            var bulletTopOffset = Math.Max(0, firstLineHeight * 0.6 - bulletLineHeight * 0.5);

            var itemContainer = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ClipToBounds = false,
                VerticalAlignment = VerticalAlignment.Top
            };

            var bulletSize = depth == 0 ? fontSize * 1.1 : fontSize * 0.75;
            var bullet = new TextBlock
            {
                Text = GetBulletSymbol(depth, list.IsOrdered, index),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, bulletTopOffset, list.IsOrdered ? 6 : 8, 0),
                FontSize = bulletSize,
                LineHeight = lineHeightPx,
                LetterSpacing = letterSpacing,
                Foreground = foreground ?? (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!
            };
            Grid.SetColumn(bullet, 0);

            var content = new StackPanel
            {
                Spacing = 4,
                ClipToBounds = false,
                VerticalAlignment = VerticalAlignment.Top
            };
            foreach (var block in item)
            {
                var blockDepth = block is ListBlock ? depth + 1 : depth;
                var rendered = await RenderBlockAsync(block, specialInlines, foreground, blockDepth);
                if (rendered != null)
                    content.Children.Add(rendered);
            }

            Grid.SetColumn(content, 1);

            itemContainer.Children.Add(bullet);
            itemContainer.Children.Add(content);
            container.Children.Add(itemContainer);

            if (list.IsOrdered)
                index++;
        }

        return container;
    }

    private async Task<Control> RenderTableAsync(Markdig.Syntax.Block table, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground)
    {
        if (table is not Markdig.Extensions.Tables.Table markdigTable)
        {
            return new TextBlock { Text = "Invalid table format" };
        }
        
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };
        
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        
        var tableBorder = new Border
        {
            BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        for (int i = 0; i < markdigTable.ColumnDefinitions.Count; i++)
        {
            var columnDef = new ColumnDefinition();
            columnDef.Width = GridLength.Auto;
            grid.ColumnDefinitions.Add(columnDef);
        }

        for (int rowIndex = 0; rowIndex < markdigTable.Count; rowIndex++)
        {
            var row = markdigTable[rowIndex];
            if (row is not Markdig.Extensions.Tables.TableRow tableRow) continue;

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool isHeaderRow = rowIndex == 0;

            for (int cellIndex = 0; cellIndex < tableRow.Count; cellIndex++)
            {
                var cell = tableRow[cellIndex];
                if (cell is not Markdig.Extensions.Tables.TableCell tableCell) continue;

                var cellBorder = new Border
                {
                    BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
                    BorderThickness = new Thickness(cellIndex == 0 ? 1 : 0, 0, 1, 1),
                    Padding = new Thickness(12, 8),
                    Background = GetCellBackground(rowIndex, isHeaderRow)
                };

                var cellContent = new StackPanel { Spacing = 4 };

                foreach (var block in tableCell)
                {
                    var rendered = await RenderBlockAsync(block, specialInlines, foreground);
                    if (rendered != null)
                    {
                        if (isHeaderRow && rendered is TextBlock headerTextBlock)
                        {
                            headerTextBlock.FontWeight = FontWeight.SemiBold;
                            headerTextBlock.Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!;
                        }
                        cellContent.Children.Add(rendered);
                    }
                }

                cellBorder.Child = cellContent;
                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, cellIndex);
                grid.Children.Add(cellBorder);
            }
        }

        scrollViewer.Content = tableBorder;
        return scrollViewer;
    }

    private IBrush GetCellBackground(int rowIndex, bool isHeaderRow)
    {
        if (isHeaderRow)
        {
            return (IBrush)Application.Current!.FindResource("CardBackgroundSecondaryBrush")!;
        }
        else
        {
            return (rowIndex % 2 == 0) 
                ? (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!
                : (IBrush)Application.Current!.FindResource("CardBackgroundPrimaryBrush")!;
        }
    }

    private void HandleLinkClick(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return;

        try
        {
            if (url.StartsWith("#"))
            {
                return;
            }

            if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("mailto:"))
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    }
                };
                process.Start();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open link {url}: {ex.Message}");
        }
    }
}