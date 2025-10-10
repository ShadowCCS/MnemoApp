using MnemoApp.Core.LaTeX.Parser;
using MnemoApp.Core.LaTeX.Layout;
using MnemoApp.Core.LaTeX.Renderer;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace MnemoApp.Core.LaTeX;

/// <summary>
/// Main entry point for LaTeX rendering.
/// Coordinates Parser -> Layout -> Renderer pipeline.
/// </summary>
public class LaTeXEngine
{
    private static readonly LRUCache<(string, double), Box> _layoutCache = new(1000);
    private static readonly LRUCache<string, LaTeXNode> _parseCache = new(500);
    private static bool _fontMetricsInitialized = false;
    private static readonly object _initLock = new();

    public static Control Render(string latex, double fontSize = 16.0)
    {
        try
        {
            // Check layout cache
            var cacheKey = (latex, fontSize);
            if (!_layoutCache.TryGetValue(cacheKey, out var layout))
            {
                // 1. Parse: LaTeX string -> AST (with parse cache)
                if (!_parseCache.TryGetValue(latex, out var ast))
                {
                    var lexer = new LaTeXLexer(latex);
                    var tokens = lexer.Tokenize();
                    var parser = new LaTeXParser(tokens);
                    ast = parser.Parse();

                    // Cache the parsed AST
                    _parseCache.Add(latex, ast);
                }

                // 2. Layout: AST -> Box model
                var layoutBuilder = new LayoutBuilder(fontSize);
                layout = layoutBuilder.BuildLayout(ast);

                // Cache the layout
                _layoutCache.Add(cacheKey, layout);
            }

            // 3. Render: Box model -> Visual
            var renderer = new LaTeXRenderer
            {
                Layout = layout
            };

            return renderer;
        }
        catch (Exception ex)
        {
            // Fallback to error display
            return new TextBlock
            {
                Text = $"LaTeX Error: {ex.Message}",
                Foreground = Avalonia.Media.Brushes.Red,
                FontSize = 12
            };
        }
    }

    public static async Task<Control> RenderAsync(string latex, double fontSize = 16.0)
    {
        // Ensure FontMetrics is initialized on UI thread before going to background
        if (!_fontMetricsInitialized)
        {
            lock (_initLock)
            {
                if (!_fontMetricsInitialized)
                {
                    // Force initialization on UI thread
                    _ = Metrics.FontMetrics.Instance;
                    _fontMetricsInitialized = true;
                }
            }
        }

        try
        {
            // Background: Parse only (cheapest operation)
            var ast = await Task.Run(() =>
            {
                if (!_parseCache.TryGetValue(latex, out var cachedAst))
                {
                    var lexer = new LaTeXLexer(latex);
                    var tokens = lexer.Tokenize();
                    var parser = new LaTeXParser(tokens);
                    cachedAst = parser.Parse();

                    _parseCache.Add(latex, cachedAst);
                }
                return cachedAst;
            });

            // UI thread: Layout (needs FormattedText) + Render
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var cacheKey = (latex, fontSize);
                if (!_layoutCache.TryGetValue(cacheKey, out var layout))
                {
                    var layoutBuilder = new LayoutBuilder(fontSize);
                    layout = layoutBuilder.BuildLayout(ast);
                    _layoutCache.Add(cacheKey, layout);
                }

                var renderer = new LaTeXRenderer
                {
                    Layout = layout
                };
                return (Control)renderer;
            });
        }
        catch (Exception ex)
        {
            return await Dispatcher.UIThread.InvokeAsync(() => new TextBlock
            {
                Text = $"LaTeX Error: {ex.Message}",
                Foreground = Avalonia.Media.Brushes.Red,
                FontSize = 12
            });
        }
    }

    public static void ClearCache()
    {
        _layoutCache.Clear();
        _parseCache.Clear();
    }
}

