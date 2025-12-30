using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.LaTeX;
using Mnemo.Infrastructure.Services.LaTeX.Parser;
using Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;
using Mnemo.Infrastructure.Services.LaTeX.Symbols;
using Mnemo.UI.Services.LaTeX.Layout;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;
using Mnemo.UI.Services.LaTeX.Metrics;
using Mnemo.UI.Controls;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace Mnemo.UI.Services;

public class LaTeXEngine : ILateXEngine
{
    private static readonly LRUCache<(string, double), Box> _layoutCache = new(1000);
    private static readonly LRUCache<string, LaTeXNode> _parseCache = new(500);
    private static bool _fontMetricsInitialized = false;
    private static readonly object _initLock = new();

    public async Task<object> RenderAsync(string tex, double fontSize = 16.0)
    {
        // Ensure FontMetrics is initialized on UI thread before going to background
        if (!_fontMetricsInitialized)
        {
            lock (_initLock)
            {
                if (!_fontMetricsInitialized)
                {
                    // Force initialization on UI thread
                    _ = FontMetrics.Instance;
                    _fontMetricsInitialized = true;
                }
            }
        }

        try
        {
            var ast = await Task.Run(() =>
            {
                if (!_parseCache.TryGetValue(tex, out var cachedAst))
                {
                    var lexer = new LaTeXLexer(tex);
                    var tokens = lexer.Tokenize();
                    var parser = new LaTeXParser(tokens);
                    cachedAst = parser.Parse();

                    _parseCache.Add(tex, cachedAst);
                }
                return cachedAst;
            }).ConfigureAwait(false);

            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var cacheKey = (tex, fontSize);
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
                return (object)renderer;
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
        FontMetrics.Instance.ClearCache();
    }
}

