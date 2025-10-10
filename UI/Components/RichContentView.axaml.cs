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

    private ContentControl? _contentHost;

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
            Render();
        }
    }

    private void Render()
    {
        if (_contentHost == null || string.IsNullOrWhiteSpace(Source))
        {
            if (_contentHost != null)
                _contentHost.Content = null;
            return;
        }

        try
        {
            // Pre-process: extract math expressions and replace with placeholders
            var (processedSource, mathExpressions) = ExtractMathExpressions(Source);
            
            var document = Markdig.Markdown.Parse(processedSource, Pipeline);
            var container = new StackPanel { Spacing = 12 };

            foreach (var block in document)
            {
                var rendered = RenderBlock(block, mathExpressions);
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
                Foreground = Brushes.Red,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    private (string processedSource, Dictionary<string, (string expr, bool isDisplay)> mathExpressions) ExtractMathExpressions(string source)
    {
        var mathExpressions = new Dictionary<string, (string, bool)>();
        var processed = source;
        var counter = 0;

        // Extract display math $$...$$
        var displayPattern = @"\$\$(.+?)\$\$";
        processed = System.Text.RegularExpressions.Regex.Replace(processed, displayPattern, match =>
        {
            var key = $"ⓂⒶⓉⒽⒹ{counter++}Ⓜ";
            mathExpressions[key] = (match.Groups[1].Value, true);
            return key;
        }, System.Text.RegularExpressions.RegexOptions.Singleline);

        // Extract inline math $...$
        var inlinePattern = @"\$(.+?)\$";
        processed = System.Text.RegularExpressions.Regex.Replace(processed, inlinePattern, match =>
        {
            var key = $"ⓂⒶⓉⒽⒾ{counter++}Ⓜ";
            mathExpressions[key] = (match.Groups[1].Value, false);
            return key;
        });

        return (processed, mathExpressions);
    }

    private Control? RenderBlock(Markdig.Syntax.Block block, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        return block switch
        {
            ParagraphBlock paragraph => RenderParagraph(paragraph, mathExpressions),
            HeadingBlock heading => RenderHeading(heading, mathExpressions),
            CodeBlock code => RenderCodeBlock(code),
            QuoteBlock quote => RenderQuote(quote, mathExpressions),
            ListBlock list => RenderList(list, mathExpressions),
            ThematicBreakBlock => new Border
            {
                Height = 1,
                Margin = new Thickness(0, 8),
                Background = (SolidColorBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!
            },
            _ => null
        };
    }

    private Control RenderParagraph(ParagraphBlock paragraph, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        if (paragraph.Inline != null && textBlock.Inlines != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                RenderInlineToInlines(inline, textBlock.Inlines, mathExpressions);
            }
        }

        return textBlock;
    }

    private void RenderInlineToInlines(Markdig.Syntax.Inlines.Inline inline, InlineCollection inlines, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                ReplaceMathPlaceholders(text, inlines, mathExpressions);
                break;

            case EmphasisInline emphasis:
                var span = new Span();
                if (emphasis.DelimiterCount == 2)
                    span.FontWeight = FontWeight.Bold;
                else if (emphasis.DelimiterCount == 1)
                    span.FontStyle = FontStyle.Italic;

                foreach (var child in emphasis)
                {
                    RenderInlineToInlines(child, span.Inlines, mathExpressions);
                }
                inlines.Add(span);
                break;

            case CodeInline code:
                inlines.Add(new Run
                {
                    Text = code.Content,
                    FontFamily = new FontFamily("Consolas,monospace"),
                    Background = new SolidColorBrush(Color.Parse("#20FFFFFF"))
                });
                break;

            case LinkInline link:
                var linkSpan = new Span
                {
                    Foreground = new SolidColorBrush(Color.Parse("#60A5FA")),
                    TextDecorations = TextDecorations.Underline
                };
                foreach (var child in link)
                {
                    RenderInlineToInlines(child, linkSpan.Inlines, mathExpressions);
                }
                inlines.Add(linkSpan);
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    RenderInlineToInlines(child, inlines, mathExpressions);
                }
                break;
        }
    }

    private void ReplaceMathPlaceholders(string text, InlineCollection inlines, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var position = 0;
        
        while (position < text.Length)
        {
            // Find the next placeholder
            int nearestIndex = -1;
            string? nearestKey = null;
            
            foreach (var kvp in mathExpressions)
            {
                var index = text.IndexOf(kvp.Key, position);
                if (index >= 0 && (nearestIndex < 0 || index < nearestIndex))
                {
                    nearestIndex = index;
                    nearestKey = kvp.Key;
                }
            }
            
            if (nearestKey != null && nearestIndex >= 0)
            {
                // Add text before placeholder
                if (nearestIndex > position)
                {
                    var beforeText = text.Substring(position, nearestIndex - position);
                    if (!string.IsNullOrWhiteSpace(beforeText))
                        inlines.Add(new Run { Text = beforeText });
                }

                // Render math
                var mathData = mathExpressions[nearestKey];
                var fontSize = mathData.isDisplay ? 18.0 : 16.0;
                var mathControl = MnemoApp.Core.LaTeX.LaTeXEngine.Render(mathData.expr, fontSize);
                inlines.Add(new InlineUIContainer { Child = mathControl });

                position = nearestIndex + nearestKey.Length;
            }
            else
            {
                // No more placeholders, add remaining text
                var remainingText = text.Substring(position);
                if (!string.IsNullOrWhiteSpace(remainingText))
                    inlines.Add(new Run { Text = remainingText });
                break;
            }
        }
    }

    private Control RenderHeading(HeadingBlock heading, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, heading.Level == 1 ? 16 : 12, 0, 8)
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

        if (heading.Inline != null && textBlock.Inlines != null)
        {
            foreach (var inline in heading.Inline)
            {
                RenderInline(inline, textBlock.Inlines);
            }
        }

        return textBlock;
    }

    private Control RenderCodeBlock(CodeBlock codeBlock)
    {
        var code = codeBlock is FencedCodeBlock fenced ? fenced.Lines.ToString() : ((LeafBlock)codeBlock).Lines.ToString();

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1A000000")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4)
        };

        var textBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas,monospace"),
            TextWrapping = TextWrapping.Wrap
        };

        border.Child = textBlock;
        return border;
    }

    private Control RenderQuote(QuoteBlock quote, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#60FFFFFF")),
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0),
            Margin = new Thickness(0, 4)
        };

        var container = new StackPanel { Spacing = 8 };

        foreach (var block in quote)
        {
            var rendered = RenderBlock(block, mathExpressions);
            if (rendered != null)
                container.Children.Add(rendered);
        }

        border.Child = container;
        return border;
    }

    private Control RenderList(ListBlock list, Dictionary<string, (string expr, bool isDisplay)> mathExpressions)
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
                MinWidth = 20
            };

            var content = new StackPanel { Spacing = 4 };
            foreach (var block in item)
            {
                var rendered = RenderBlock(block, mathExpressions);
                if (rendered != null)
                    content.Children.Add(rendered);
            }

            itemContainer.Children.Add(bullet);
            itemContainer.Children.Add(content);
            container.Children.Add(itemContainer);
        }

        return container;
    }

    private void RenderInline(Markdig.Syntax.Inlines.Inline inline, InlineCollection inlines)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                RenderTextWithMath(text, inlines);
                break;

            case EmphasisInline emphasis:
                var run = new Span();
                if (emphasis.DelimiterCount == 2)
                    run.FontWeight = FontWeight.Bold;
                else if (emphasis.DelimiterCount == 1)
                    run.FontStyle = FontStyle.Italic;

                foreach (var child in emphasis)
                {
                    RenderInline(child, run.Inlines);
                }
                inlines.Add(run);
                break;

            case CodeInline code:
                inlines.Add(new Run
                {
                    Text = code.Content,
                    FontFamily = new FontFamily("Consolas,monospace"),
                    Background = new SolidColorBrush(Color.Parse("#20FFFFFF"))
                });
                break;

            case LinkInline link:
                var linkSpan = new Span
                {
                    Foreground = new SolidColorBrush(Color.Parse("#60A5FA")),
                    TextDecorations = TextDecorations.Underline
                };

                foreach (var child in link)
                {
                    RenderInline(child, linkSpan.Inlines);
                }
                inlines.Add(linkSpan);
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    RenderInline(child, inlines);
                }
                break;
        }
    }

    private void RenderTextWithMath(string text, InlineCollection inlines)
    {
        var position = 0;
        
        while (position < text.Length)
        {
            // Check for display math $$...$$
            var displayStart = text.IndexOf("$$", position);
            if (displayStart >= 0 && displayStart >= position)
            {
                var displayEnd = text.IndexOf("$$", displayStart + 2);
                if (displayEnd > displayStart)
                {
                    // Add text before math
                    if (displayStart > position)
                    {
                        inlines.Add(new Run { Text = text.Substring(position, displayStart - position) });
                    }

                    // Render display math (block-level)
                    var mathExpr = text.Substring(displayStart + 2, displayEnd - displayStart - 2).Trim();
                    if (!string.IsNullOrWhiteSpace(mathExpr))
                    {
                        var mathControl = LaTeXEngine.Render(mathExpr, 18.0);
                        var container = new InlineUIContainer
                        {
                            Child = mathControl
                        };
                        inlines.Add(container);
                    }

                    position = displayEnd + 2;
                    continue;
                }
            }

            // Check for inline math $...$
            var inlineStart = text.IndexOf("$", position);
            if (inlineStart >= 0 && inlineStart >= position)
            {
                var inlineEnd = text.IndexOf("$", inlineStart + 1);
                if (inlineEnd > inlineStart)
                {
                    // Add text before math
                    if (inlineStart > position)
                    {
                        inlines.Add(new Run { Text = text.Substring(position, inlineStart - position) });
                    }

                    // Render inline math
                    var mathExpr = text.Substring(inlineStart + 1, inlineEnd - inlineStart - 1).Trim();
                    if (!string.IsNullOrWhiteSpace(mathExpr))
                    {
                        var mathControl = LaTeXEngine.Render(mathExpr, 16.0);
                        var container = new InlineUIContainer
                        {
                            Child = mathControl
                        };
                        inlines.Add(container);
                    }

                    position = inlineEnd + 1;
                    continue;
                }
            }

            // No more math found, add remaining text
            inlines.Add(new Run { Text = text.Substring(position) });
            break;
        }
    }
}

