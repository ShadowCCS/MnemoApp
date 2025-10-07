using MnemoApp.Core.LaTeX.Parser;
using MnemoApp.Core.LaTeX.Layout;
using MnemoApp.Core.LaTeX.Renderer;
using Avalonia.Controls;
using System;
using System.Collections.Generic;

namespace MnemoApp.Core.LaTeX;

/// <summary>
/// Main entry point for LaTeX rendering.
/// Coordinates Parser -> Layout -> Renderer pipeline.
/// </summary>
public class LaTeXEngine
{
    private static readonly Dictionary<(string, double), Box> _layoutCache = new();
    private static readonly Dictionary<string, LaTeXNode> _parseCache = new();
    private const int MaxCacheSize = 1000;

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
                    if (_parseCache.Count >= MaxCacheSize)
                    {
                        // Simple eviction: clear oldest half
                        var toRemove = _parseCache.Count / 2;
                        var keys = new List<string>(_parseCache.Keys);
                        for (int i = 0; i < toRemove; i++)
                        {
                            _parseCache.Remove(keys[i]);
                        }
                    }
                    _parseCache[latex] = ast;
                }

                // 2. Layout: AST -> Box model
                var layoutBuilder = new LayoutBuilder(fontSize);
                layout = layoutBuilder.BuildLayout(ast);

                // Cache the layout
                if (_layoutCache.Count >= MaxCacheSize)
                {
                    // Simple eviction: clear oldest half
                    var toRemove = _layoutCache.Count / 2;
                    var keys = new List<(string, double)>(_layoutCache.Keys);
                    for (int i = 0; i < toRemove; i++)
                    {
                        _layoutCache.Remove(keys[i]);
                    }
                }
                _layoutCache[cacheKey] = layout;
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

    public static void ClearCache()
    {
        _layoutCache.Clear();
        _parseCache.Clear();
    }
}

