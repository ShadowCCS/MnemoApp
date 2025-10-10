using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Markdig;
using Markdown.Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Linq;
using System.Collections.Generic;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MnemoApp.Core.LaTeX;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace MnemoApp.UI.Components;

public partial class RichContentView : UserControl
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<RichContentView, string?>(nameof(Source));

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    // Compiled regex patterns for performance
    private static readonly Regex DisplayMathRegex = new(@"\$\$(.+?)\$\$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InlineMathRegex = new(@"\$(.+?)\$", RegexOptions.Compiled);

    private ContentControl? _contentHost;
    private bool _isRendering = false;

    public RichContentView()
    {
        InitializeComponent();
        _contentHost = this.FindControl<ContentControl>("ContentHost");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
        {
            _ = RenderAsync();
        }
    }

    private async Task RenderAsync()
    {
        if (_contentHost == null || string.IsNullOrWhiteSpace(Source))
        {
            if (_contentHost != null)
                _contentHost.Content = null;
            return;
        }

        // Prevent concurrent renders
        if (_isRendering)
            return;

        _isRendering = true;

        try
        {
            // Show loading placeholder
            _contentHost.Content = new TextBlock
            {
                Text = "Loading...",
                Foreground = (IBrush)Application.Current!.FindResource("TextTertiaryBrush")!,
                FontStyle = FontStyle.Italic
            };

            var source = Source;

            // Background: Parse markdown + extract math
            var (document, mathExpressions) = await Task.Run(() =>
            {
                var (processedSource, mathExprs) = ExtractMathExpressions(source);
                var doc = Markdig.Markdown.Parse(processedSource, Pipeline);
                return (doc, mathExprs);
            });

            // UI thread: Render blocks
            var container = new StackPanel { Spacing = 12 };

            foreach (var block in document)
            {
                var rendered = await RenderBlockAsync(block, mathExpressions);
                if (rendered != null)
                    container.Children.Add(rendered);
            }

            _contentHost.Content = container;
        }
        catch (Exception ex)
        {
            _contentHost.Content = new TextBlock
            {
                Text = $"Error rendering content: {ex.Message}",
                Foreground = (IBrush)Application.Current!.FindResource("SystemErrorBackgroundBrush")!,
                TextWrapping = TextWrapping.Wrap
            };
        }
        finally
        {
            _isRendering = false;
        }
    }

    private (string processedSource, Dictionary<string, (string expr, bool isDisplay)> mathExpressions) ExtractMathExpressions(string source)
    {
        var mathExpressions = new Dictionary<string, (string, bool)>();
        var counter = 0;

        // Extract display math $$...$$ using compiled regex
        var processed = DisplayMathRegex.Replace(source, match =>
        {
            var key = $"ⓂⒶⓉⒽⒹ{counter++}Ⓜ";
            mathExpressions[key] = (match.Groups[1].Value, true);
            return key;
        });

        // Extract inline math $...$ using compiled regex
        processed = InlineMathRegex.Replace(processed, match =>
        {
            var key = $"ⓂⒶⓉⒽⒾ{counter++}Ⓜ";
            mathExpressions[key] = (match.Groups[1].Value, false);
            return key;
        });

        return (processed, mathExpressions);
    }

    private async Task<Control?> RenderBlockAsync(Markdig.Syntax.Block block, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        return block switch
        {
            ParagraphBlock paragraph => await RenderParagraphAsync(paragraph, mathExpressions),
            HeadingBlock heading => await RenderHeadingAsync(heading, mathExpressions),
            CodeBlock code => RenderCodeBlock(code),
            QuoteBlock quote => await RenderQuoteAsync(quote, mathExpressions),
            ListBlock list => await RenderListAsync(list, mathExpressions),
            ThematicBreakBlock => new Border
            {
                Height = 1,
                Margin = new Thickness(0, 8),
                Background = (SolidColorBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!
            },
            _ => null
        };
    }

    private async Task<Control> RenderParagraphAsync(ParagraphBlock paragraph, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!
        };

        if (paragraph.Inline != null && textBlock.Inlines != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                await RenderInlineToInlinesAsync(inline, textBlock.Inlines, mathExpressions);
            }
        }

        return textBlock;
    }

    private async Task RenderInlineToInlinesAsync(Markdig.Syntax.Inlines.Inline inline, InlineCollection inlines, Dictionary<string, (string expr, bool isDisplay)> mathExpressions, bool isHeading = false)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                await ReplaceMathPlaceholdersAsync(text, inlines, mathExpressions, isHeading);
                break;

            case EmphasisInline emphasis:
                var span = new Span();
                if (emphasis.DelimiterCount == 2)
                    span.FontWeight = FontWeight.Bold;
                else if (emphasis.DelimiterCount == 1)
                    span.FontStyle = FontStyle.Italic;

                foreach (var child in emphasis)
                {
                    await RenderInlineToInlinesAsync(child, span.Inlines, mathExpressions, isHeading);
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
                var linkSpan = new Span
                {
                    Foreground = (IBrush)Application.Current!.FindResource("LinksBrush")!,
                    TextDecorations = TextDecorations.Underline
                };
                foreach (var child in link)
                {
                    await RenderInlineToInlinesAsync(child, linkSpan.Inlines, mathExpressions, isHeading);
                }
                inlines.Add(linkSpan);
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    await RenderInlineToInlinesAsync(child, inlines, mathExpressions, isHeading);
                }
                break;
        }
    }

    // OPTIMIZED: O(n) single-pass placeholder search instead of O(n×m)
    private async Task ReplaceMathPlaceholdersAsync(string text, InlineCollection inlines, Dictionary<string, (string expr, bool isDisplay)> mathExpressions, bool isHeading = false)
    {
        if (string.IsNullOrEmpty(text) || mathExpressions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(text))
                inlines.Add(new Run { Text = text, Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")! });
            return;
        }

        var position = 0;
        var textLength = text.Length;
        
        while (position < textLength)
        {
            // Single-pass: find next placeholder by scanning for marker prefix
            var markerIndex = text.IndexOf("Ⓜ", position, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
            // No more placeholders, add remaining text
            var remainingText = text.Substring(position);
            if (!string.IsNullOrWhiteSpace(remainingText))
                inlines.Add(new Run { Text = remainingText, Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")! });
                break;
            }

            // Extract potential placeholder key
            var endMarkerIndex = text.IndexOf("Ⓜ", markerIndex + 1, StringComparison.Ordinal);
            if (endMarkerIndex < 0)
            {
                // Incomplete marker, treat as text
                var remainingText = text.Substring(position);
                if (!string.IsNullOrWhiteSpace(remainingText))
                    inlines.Add(new Run { Text = remainingText, Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")! });
                break;
            }

            var potentialKey = text.Substring(markerIndex, endMarkerIndex - markerIndex + 1);
            
            // Check if this is a valid placeholder
            if (mathExpressions.TryGetValue(potentialKey, out var mathData))
            {
                // Add text before placeholder
                if (markerIndex > position)
                {
                    var beforeText = text.Substring(position, markerIndex - position);
                    if (!string.IsNullOrWhiteSpace(beforeText))
                        inlines.Add(new Run { Text = beforeText, Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")! });
                }

                // Render math asynchronously
                var fontSize = mathData.isDisplay ? 18.0 : 16.0;
                var mathControl = await LaTeXEngine.RenderAsync(mathData.expr, fontSize);
                inlines.Add(new InlineUIContainer { Child = mathControl });

                position = endMarkerIndex + 1;
            }
            else
            {
                // Not a valid placeholder, continue searching
                position = markerIndex + 1;
            }
        }
    }

    private async Task<Control> RenderHeadingAsync(HeadingBlock heading, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, heading.Level == 1 ? 16 : 12, 0, 8),
            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
        };

        textBlock.FontSize = heading.Level switch
        {
            1 => 32,
            2 => 24,
            3 => 20,
            4 => 18,
            5 => 16,
            _ => 14
        };

        // UNIFIED: Use same placeholder-based rendering as paragraphs
        if (heading.Inline != null && textBlock.Inlines != null)
        {
            foreach (var inline in heading.Inline)
            {
                await RenderInlineToInlinesAsync(inline, textBlock.Inlines, mathExpressions, isHeading: true);
            }
        }

        return textBlock;
    }

    private Control RenderCodeBlock(CodeBlock codeBlock)
    {
        var code = codeBlock is FencedCodeBlock fenced ? fenced.Lines.ToString() : ((LeafBlock)codeBlock).Lines.ToString();

        var border = new Border
        {
            Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4)
        };

        var textBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas,monospace"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
        };

        border.Child = textBlock;
        return border;
    }

    private async Task<Control> RenderQuoteAsync(QuoteBlock quote, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
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
            var rendered = await RenderBlockAsync(block, mathExpressions);
            if (rendered != null)
                container.Children.Add(rendered);
        }

        border.Child = container;
        return border;
    }

    private async Task<Control> RenderListAsync(ListBlock list, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        var container = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4) };
        int index = 1;

        foreach (var item in list.Cast<ListItemBlock>())
        {
            var itemContainer = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };

            var bullet = new TextBlock
            {
                Text = list.IsOrdered ? $"{index++}." : "•",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                MinWidth = 20,
                Foreground = (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!
            };

            var content = new StackPanel { Spacing = 4 };
            foreach (var block in item)
            {
                var rendered = await RenderBlockAsync(block, mathExpressions);
                if (rendered != null)
                    content.Children.Add(rendered);
            }

            itemContainer.Children.Add(bullet);
            itemContainer.Children.Add(content);
            container.Children.Add(itemContainer);
        }

        return container;
    }

}

