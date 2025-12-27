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
using Markdig.Extensions.Tables;
using Mnemo.Core.Services;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Mnemo.UI.Components;

public partial class RichContentView : UserControl
{
    private readonly ILocalizationService _loc;
    private readonly ILateXEngine _latexEngine;
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
    private static readonly Regex HighlightRegex = new(@"==(.+?)==", RegexOptions.Compiled);
    private static readonly Regex StrikethroughRegex = new(@"~~(.+?)~~", RegexOptions.Compiled);
    // LaTeX-style subscript and superscript: H_{2}O or H_2O, x^{2} or x^2
    private static readonly Regex SubscriptRegex = new(@"_(\{[^}]+?\}|\w)", RegexOptions.Compiled);
    private static readonly Regex SuperscriptRegex = new(@"\^(\{[^}]+?\}|\w)", RegexOptions.Compiled);
    // Markdown-style subscript and superscript: H~2~O and E=mc^2^
    private static readonly Regex TildeSubscriptRegex = new(@"~([^~]+)~", RegexOptions.Compiled);
    private static readonly Regex CaretSuperscriptRegex = new(@"\^([^^\s]+)\^", RegexOptions.Compiled);

    private ContentControl? _contentHost;
    private bool _isRendering = false;

    public RichContentView()
    {
        var sp = ((App)Application.Current!).Services!;
        _loc = sp.GetRequiredService<ILocalizationService>();
        _latexEngine = sp.GetRequiredService<ILateXEngine>();
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
                Text = _loc.T("Loading", "Common"),
                Foreground = (IBrush)Application.Current!.FindResource("TextTertiaryBrush")!,
                FontStyle = FontStyle.Italic
            };

            var source = Source;

            // Background: Parse markdown + extract special inline elements
            var (document, specialInlines) = await Task.Run(() =>
            {
                var (processedSource, inlines) = ExtractSpecialInlines(source);
                var doc = Markdig.Markdown.Parse(processedSource, Pipeline);
                return (doc, inlines);
            });

            // UI thread: Render blocks
            var container = new StackPanel { Spacing = 12 };

            foreach (var block in document)
            {
                var rendered = await RenderBlockAsync(block, specialInlines);
                if (rendered != null)
                    container.Children.Add(rendered);
            }

            _contentHost.Content = container;
        }
        catch (Exception ex)
        {
            _contentHost.Content = new TextBlock
            {
                Text = $"{_loc.T("ErrorRendering", "RichText")}: {ex.Message}",
                Foreground = (IBrush)Application.Current!.FindResource("SystemErrorBackgroundBrush")!,
                TextWrapping = TextWrapping.Wrap
            };
        }
        finally
        {
            _isRendering = false;
        }
    }

    private (string processedSource, Dictionary<string, (string content, InlineType type)> inlines) ExtractSpecialInlines(string source)
    {
        var inlines = new Dictionary<string, (string, InlineType)>();
        var counter = 0;
        

        // Extract display math $$...$$ first (must be before inline math)
        var processed = DisplayMathRegex.Replace(source, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = (match.Groups[1].Value, InlineType.DisplayMath);
            return key;
        });

        // Extract inline math $...$
        processed = InlineMathRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = (match.Groups[1].Value, InlineType.InlineMath);
            return key;
        });

        // Extract highlighted text ==...==
        processed = HighlightRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = (match.Groups[1].Value, InlineType.Highlight);
            return key;
        });

        // Extract strikethrough ~~...~~ (MUST be before tilde subscript!)
        processed = StrikethroughRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = (match.Groups[1].Value, InlineType.Strikethrough);
            return key;
        });

        // Extract caret superscript ^...^ (MUST be before LaTeX superscript!)
        processed = CaretSuperscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            var content = match.Groups[1].Value;
            inlines[key] = (content, InlineType.Superscript);
            return key;
        });

        // Extract LaTeX superscript ^{...} or ^x
        processed = SuperscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            var content = match.Groups[1].Value;
            // Strip braces if present
            if (content.StartsWith("{") && content.EndsWith("}"))
                content = content.Substring(1, content.Length - 2);
            inlines[key] = (content, InlineType.Superscript);
            return key;
        });

        // Extract tilde subscript ~...~ (after strikethrough to avoid conflicts)
        processed = TildeSubscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            var content = match.Groups[1].Value;
            inlines[key] = (content, InlineType.Subscript);
            return key;
        });

        // Extract LaTeX subscript _{...} or _x
        processed = SubscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            var content = match.Groups[1].Value;
            // Strip braces if present
            if (content.StartsWith("{") && content.EndsWith("}"))
                content = content.Substring(1, content.Length - 2);
            inlines[key] = (content, InlineType.Subscript);
            return key;
        });

        
        return (processed, inlines);
    }

    private enum InlineType
    {
        DisplayMath,
        InlineMath,
        Highlight,
        Strikethrough,
        Superscript,
        Subscript
    }

    private async Task<Control?> RenderBlockAsync(Markdig.Syntax.Block block, Dictionary<string, (string content, InlineType type)> specialInlines)
    {
        return block switch
        {
            ParagraphBlock paragraph => await RenderParagraphAsync(paragraph, specialInlines),
            HeadingBlock heading => await RenderHeadingAsync(heading, specialInlines),
            CodeBlock code => RenderCodeBlock(code),
            QuoteBlock quote => await RenderQuoteAsync(quote, specialInlines),
            ListBlock list => await RenderListAsync(list, specialInlines),
            var table when table.GetType().Name == "Table" => await RenderTableAsync(table, specialInlines),
            ThematicBreakBlock => new Border
            {
                Height = 1,
                Margin = new Thickness(0, 8),
                Background = (SolidColorBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!
            },
            _ => null
        };
    }

    private async Task<Control> RenderParagraphAsync(ParagraphBlock paragraph, Dictionary<string, (string content, InlineType type)> specialInlines)
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
                await RenderInlineToInlinesAsync(inline, textBlock.Inlines, specialInlines);
            }
        }

        return textBlock;
    }

    private async Task RenderInlineToInlinesAsync(Markdig.Syntax.Inlines.Inline inline, InlineCollection inlines, Dictionary<string, (string content, InlineType type)> specialInlines, bool isHeading = false)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                await ReplaceSpecialPlaceholdersAsync(text, inlines, specialInlines, isHeading);
                break;

            case EmphasisInline emphasis:
                var span = new Span();
                if (emphasis.DelimiterCount == 2)
                    span.FontWeight = FontWeight.Bold;
                else if (emphasis.DelimiterCount == 1)
                    span.FontStyle = FontStyle.Italic;

                foreach (var child in emphasis)
                {
                    await RenderInlineToInlinesAsync(child, span.Inlines, specialInlines, isHeading);
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
                // Create a clickable link using a Button with transparent styling
                var linkButton = new Button
                {
                    Background = Avalonia.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                
                // Create the link content
                var linkContent = new TextBlock
                {
                    Foreground = (IBrush)Application.Current!.FindResource("LinksBrush")!,
                    TextDecorations = TextDecorations.Underline,
                    Background = Avalonia.Media.Brushes.Transparent
                };
                
                // Add the link text
                if (link.FirstChild is LiteralInline linkLiteral)
                {
                    linkContent.Text = linkLiteral.Content.ToString();
                }
                else
                {
                    // Handle complex link content
                    foreach (var child in link)
                    {
                        if (linkContent.Inlines != null)
                        {
                            await RenderInlineToInlinesAsync(child, linkContent.Inlines, specialInlines, isHeading);
                        }
                    }
                }
                
                linkButton.Content = linkContent;
                
                // Add click handler
                linkButton.Click += (sender, e) =>
                {
                    HandleLinkClick(link.Url);
                    e.Handled = true;
                };
                
                // Wrap the button in an InlineUIContainer
                inlines.Add(new InlineUIContainer { Child = linkButton });
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    await RenderInlineToInlinesAsync(child, inlines, specialInlines, isHeading);
                }
                break;
        }
    }

    // OPTIMIZED: O(n) single-pass placeholder search instead of O(n×m)
    private async Task ReplaceSpecialPlaceholdersAsync(string text, InlineCollection inlines, Dictionary<string, (string content, InlineType type)> specialInlines, bool isHeading = false)
    {
        if (string.IsNullOrEmpty(text) || specialInlines.Count == 0)
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
            var markerIndex = text.IndexOf("Ⓢ", position, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                // No more placeholders, add remaining text
                var remainingText = text.Substring(position);
                if (!string.IsNullOrWhiteSpace(remainingText))
                    inlines.Add(new Run { Text = remainingText, Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")! });
                break;
            }

            // Extract potential placeholder key
            var endMarkerIndex = text.IndexOf("Ⓢ", markerIndex + 1, StringComparison.Ordinal);
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
            if (specialInlines.TryGetValue(potentialKey, out var inlineData))
            {
                // Add text before placeholder
                if (markerIndex > position)
                {
                    var beforeText = text.Substring(position, markerIndex - position);
                    if (!string.IsNullOrWhiteSpace(beforeText))
                        inlines.Add(new Run { Text = beforeText, Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")! });
                }

                // Render the special inline based on type
                switch (inlineData.type)
                {
                    case InlineType.DisplayMath:
                        var displayMathControl = await _latexEngine.RenderAsync(inlineData.content, 18.0);
                        inlines.Add(new InlineUIContainer { Child = displayMathControl });
                        break;

                    case InlineType.InlineMath:
                        var inlineMathControl = await _latexEngine.RenderAsync(inlineData.content, 16.0);
                        inlines.Add(new InlineUIContainer { Child = inlineMathControl });
                        break;

                    case InlineType.Highlight:
                        inlines.Add(new Run
                        {
                            Text = inlineData.content,
                            Background = (IBrush)Application.Current!.FindResource("HighlightedTextBrush")!,
                            Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")!
                        });
                        break;

                    case InlineType.Superscript:
                        var superscriptTextBlock = new TextBlock
                        {
                            Text = inlineData.content,
                            FontSize = 10,
                            Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")!,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                            Margin = new Thickness(0, -2, 0, 0)
                        };
                        inlines.Add(new InlineUIContainer { Child = superscriptTextBlock });
                        break;

                    case InlineType.Subscript:
                        var subscriptTextBlock = new TextBlock
                        {
                            Text = inlineData.content,
                            FontSize = 10,
                            Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")!,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                            Margin = new Thickness(0, 0, 0, -2)
                        };
                        inlines.Add(new InlineUIContainer { Child = subscriptTextBlock });
                        break;

                    case InlineType.Strikethrough:
                        inlines.Add(new Run
                        {
                            Text = inlineData.content,
                            TextDecorations = TextDecorations.Strikethrough,
                            Foreground = (IBrush)Application.Current!.FindResource(isHeading ? "TextPrimaryBrush" : "TextSecondaryBrush")!
                        });
                        break;
                }

                position = endMarkerIndex + 1;
            }
            else
            {
                // Not a valid placeholder, continue searching
                position = markerIndex + 1;
            }
        }
    }

    private async Task<Control> RenderHeadingAsync(HeadingBlock heading, Dictionary<string, (string content, InlineType type)> specialInlines)
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
                await RenderInlineToInlinesAsync(inline, textBlock.Inlines, specialInlines, isHeading: true);
            }
        }

        return textBlock;
    }

    private Control RenderCodeBlock(CodeBlock codeBlock)
    {
        var fenced = codeBlock as FencedCodeBlock;
        var language = fenced?.Info ?? string.Empty;
        var code = fenced?.Lines.ToString() ?? ((LeafBlock)codeBlock).Lines.ToString();

        var container = new Border
        {
            Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
            BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 8)
        };

        var stackPanel = new StackPanel();

        // Language indicator header
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
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            var copyButton = new Button
            {
                Content = _loc.T("Copy", "Common"),
                FontSize = 11,
                Padding = new Thickness(8, 4),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
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
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(code);
                    }
                }
                catch
                {
                    // Clipboard access might fail in some contexts
                }
            };

            headerPanel.Children.Add(languageLabel);
            headerPanel.Children.Add(copyButton);
            stackPanel.Children.Add(headerPanel);
        }
        else
        {
            // Copy button for non-fenced code blocks
            var copyButton = new Button
            {
                Content = _loc.T("Copy", "Common"),
                FontSize = 11,
                Padding = new Thickness(8, 4),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
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
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(code);
                    }
                }
                catch
                {
                    // Clipboard access might fail in some contexts
                }
            };

            stackPanel.Children.Add(copyButton);
        }

        // Code content with syntax highlighting
        var codeTextBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("JetBrains Mono, Consolas, 'Courier New', monospace"),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!,
            Padding = new Thickness(12),
            Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!
        };

        // Apply basic syntax highlighting based on language
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
        // Basic syntax highlighting using Run elements
        var inlines = new InlineCollection();
        var lines = code.Split('\n');
        
        foreach (var line in lines)
        {
            var processedLine = ProcessLineForSyntaxHighlighting(line, language);
            inlines.AddRange(processedLine);
            inlines.Add(new LineBreak());
        }

        // Remove the last line break
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
        
        // Language-specific highlighting patterns
        var patterns = GetHighlightingPatterns(language);
        
        foreach (var (pattern, brushKey) in patterns)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(currentText, pattern);
            if (matches.Count > 0)
            {
                var lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    // Add text before match
                    if (match.Index > lastIndex)
                    {
                        inlines.Add(new Avalonia.Controls.Documents.Run 
                        { 
                            Text = currentText.Substring(lastIndex, match.Index - lastIndex),
                            Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
                        });
                    }
                    
                    // Add highlighted match
                    inlines.Add(new Avalonia.Controls.Documents.Run 
                    { 
                        Text = match.Value,
                        Foreground = (IBrush)Application.Current!.FindResource(brushKey)!
                    });
                    
                    lastIndex = match.Index + match.Length;
                }
                
                // Add remaining text
                if (lastIndex < currentText.Length)
                {
                    inlines.Add(new Avalonia.Controls.Documents.Run 
                    { 
                        Text = currentText.Substring(lastIndex),
                        Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!
                    });
                }
                return inlines;
            }
        }
        
        // No highlighting applied, return plain text
        inlines.Add(new Avalonia.Controls.Documents.Run 
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
            _ => new List<(string, string)>()
        };
    }

    private async Task<Control> RenderQuoteAsync(QuoteBlock quote, Dictionary<string, (string content, InlineType type)> specialInlines)
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
            var rendered = await RenderBlockAsync(block, specialInlines);
            if (rendered != null)
                container.Children.Add(rendered);
        }

        border.Child = container;
        return border;
    }

    private async Task<Control> RenderListAsync(ListBlock list, Dictionary<string, (string content, InlineType type)> specialInlines)
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
                var rendered = await RenderBlockAsync(block, specialInlines);
                if (rendered != null)
                    content.Children.Add(rendered);
            }

            itemContainer.Children.Add(bullet);
            itemContainer.Children.Add(content);
            container.Children.Add(itemContainer);
        }

        return container;
    }

    private async Task<Control> RenderTableAsync(Markdig.Syntax.Block table, Dictionary<string, (string content, InlineType type)> specialInlines)
    {
        if (table is not Markdig.Extensions.Tables.Table markdigTable)
        {
            System.Diagnostics.Debug.WriteLine($"Table is not a Markdig Table type: {table.GetType().Name}");
            return new TextBlock { Text = _loc.T("InvalidTableFormat", "RichText") };
        }

        System.Diagnostics.Debug.WriteLine($"Rendering table with {markdigTable.Count} rows and {markdigTable.ColumnDefinitions.Count} columns");
        
        var grid = new Grid
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        
        // Create a scrollable container for the table
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        
        var tableBorder = new Border
        {
            BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = (IBrush)Application.Current!.FindResource("TextControlBackgroundBrush")!,
            Child = grid,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };

        // Set up columns with content-based sizing
        for (int i = 0; i < markdigTable.ColumnDefinitions.Count; i++)
        {
            var columnDef = new ColumnDefinition();
            // Use Auto sizing to fit content, preventing unnecessary width expansion
            columnDef.Width = GridLength.Auto;
            grid.ColumnDefinitions.Add(columnDef);
        }

        // Process each row
        for (int rowIndex = 0; rowIndex < markdigTable.Count; rowIndex++)
        {
            var row = markdigTable[rowIndex];
            if (row is not Markdig.Extensions.Tables.TableRow tableRow)
            {
                System.Diagnostics.Debug.WriteLine($"Row {rowIndex} is not a TableRow: {row.GetType().Name}");
                continue;
            }

            // Add row definition
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            System.Diagnostics.Debug.WriteLine($"Processing row {rowIndex} with {tableRow.Count} cells");

            // Determine if this is a header row (first row)
            bool isHeaderRow = rowIndex == 0;

            // Process each cell in the row
            for (int cellIndex = 0; cellIndex < tableRow.Count; cellIndex++)
            {
                var cell = tableRow[cellIndex];
                if (cell is not Markdig.Extensions.Tables.TableCell tableCell)
                {
                    System.Diagnostics.Debug.WriteLine($"Cell {cellIndex} is not a TableCell: {cell.GetType().Name}");
                    continue;
                }

                // Create cell border with improved styling
                var cellBorder = new Border
                {
                    BorderBrush = (IBrush)Application.Current!.FindResource("RichTextSeparationLineBrush")!,
                    BorderThickness = new Thickness(0, 0, 1, 1), // Right and bottom borders only
                    Padding = new Thickness(12, 8),
                    Background = GetCellBackground(rowIndex, isHeaderRow)
                };

                // Add left border for first column
                if (cellIndex == 0)
                {
                    cellBorder.BorderThickness = new Thickness(1, 0, 1, 1);
                }

                var cellContent = new StackPanel { Spacing = 4 };

                // Render cell content with appropriate styling for headers
                foreach (var block in tableCell)
                {
                    var rendered = await RenderBlockAsync(block, specialInlines);
                    if (rendered != null)
                    {
                        // Apply header styling to text blocks in header row
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
            // Header row gets a distinct background
            return (IBrush)Application.Current!.FindResource("CardBackgroundSecondaryBrush")!;
        }
        else
        {
            // Alternating row colors for data rows
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
            // Check if it's an internal link (starts with #)
            if (url.StartsWith("#"))
            {
                // Handle internal navigation if needed
                // For now, just log it
                System.Diagnostics.Debug.WriteLine($"Internal link clicked: {url}");
                return;
            }

            // Handle external links
            if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("mailto:"))
            {
                // Use the system to open the URL
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


