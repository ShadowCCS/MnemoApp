using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Mnemo.UI.Services;

/// <summary>
/// Tokenizes with TextMateSharp.Grammars; colors come from app theme <c>SyntaxCode*Brush</c> resources.
/// </summary>
public sealed class TextMateSyntaxHighlighter : ITextMateSyntaxHighlighter
{
    private readonly object _gate = new();
    private Registry? _registry;
    private readonly Dictionary<string, IGrammar?> _grammarByScope = new(StringComparer.Ordinal);

    /// <summary>Markdown / fence aliases → VS Code <c>language id</c> understood by <see cref="RegistryOptions.GetScopeByLanguageId"/>.</summary>
    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["js"] = "javascript",
        ["mjs"] = "javascript",
        ["cjs"] = "javascript",
        ["ts"] = "typescript",
        ["tsx"] = "typescript",
        ["jsx"] = "javascript",
        ["cs"] = "csharp",
        ["py"] = "python",
        ["rb"] = "ruby",
        ["rs"] = "rust",
        ["go"] = "go",
        ["sh"] = "shellscript",
        ["bash"] = "shellscript",
        ["zsh"] = "shellscript",
        ["ps1"] = "powershell",
        ["psm1"] = "powershell",
        ["yml"] = "yaml",
        ["md"] = "markdown",
        ["c"] = "c",
        ["h"] = "c",
        ["cc"] = "cpp",
        ["cxx"] = "cpp",
        ["cpp"] = "cpp",
        ["hpp"] = "cpp",
        ["sql"] = "sql",
        ["htm"] = "html",
        ["dockerfile"] = "dockerfile",
    };

    public void ApplyToTextBlock(TextBlock target, string code, string? languageFenceId, IBrush defaultForeground)
    {
        target.Inlines?.Clear();

        if (SketchSyntaxHighlighter.IsSketchLanguage(languageFenceId))
        {
            SketchSyntaxHighlighter.ApplyToTextBlock(target, code ?? string.Empty, defaultForeground);
            return;
        }

        var registry = EnsureRegistry();
        var grammar = ResolveGrammar(registry, languageFenceId);
        if (grammar == null || string.IsNullOrEmpty(code))
        {
            target.Inlines!.Add(new Run { Text = code ?? string.Empty, Foreground = defaultForeground });
            return;
        }

        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        IStateStack? ruleStack = null;

        try
        {
            foreach (var line in lines)
            {
                var result = grammar.TokenizeLine(line, ruleStack, TimeSpan.FromSeconds(4));
                ruleStack = result.RuleStack;

                foreach (var token in result.Tokens)
                {
                    var start = Math.Clamp(token.StartIndex, 0, line.Length);
                    var end = Math.Clamp(token.EndIndex, start, line.Length);
                    if (end <= start)
                        continue;

                    var slice = line.Substring(start, end - start);
                    var scopes = CoerceScopes(token.Scopes);
                    var brush = SyntaxCodeBrushResolver.Resolve(scopes, defaultForeground);
                    target.Inlines!.Add(new Run { Text = slice, Foreground = brush });
                }

                target.Inlines!.Add(new LineBreak());
            }

            if (target.Inlines!.Count > 0 && target.Inlines[^1] is LineBreak)
                target.Inlines.RemoveAt(target.Inlines.Count - 1);
        }
        catch
        {
            target.Inlines!.Clear();
            target.Inlines.Add(new Run { Text = code, Foreground = defaultForeground });
        }
    }

    private Registry EnsureRegistry()
    {
        lock (_gate)
        {
            if (_registry != null)
                return _registry;

            var options = new RegistryOptions(ThemeName.DarkPlus);
            _registry = new Registry(options);
            return _registry;
        }
    }

    private IGrammar? ResolveGrammar(Registry registry, string? languageFenceId)
    {
        var lang = (languageFenceId ?? string.Empty).Trim();
        if (lang.Length == 0)
            return null;

        if (LanguageAliases.TryGetValue(lang, out var mapped))
            lang = mapped;

        var opts = (RegistryOptions)registry.GetLocator();
        var scope = opts.GetScopeByLanguageId(lang);
        if (string.IsNullOrEmpty(scope) && lang.Length > 0 && lang[0] == '.')
            scope = opts.GetScopeByExtension(lang);
        if (string.IsNullOrEmpty(scope) && lang.Length > 0)
            scope = opts.GetScopeByExtension("." + lang);

        if (string.IsNullOrEmpty(scope))
            return null;

        lock (_gate)
        {
            if (_grammarByScope.TryGetValue(scope, out var cached))
                return cached;

            try
            {
                var g = registry.LoadGrammar(scope);
                _grammarByScope[scope] = g;
                return g;
            }
            catch
            {
                _grammarByScope[scope] = null;
                return null;
            }
        }
    }

    private static List<string> CoerceScopes(IList? raw)
    {
        var list = new List<string>();
        if (raw == null) return list;
        foreach (var o in raw)
        {
            if (o is string s && s.Length > 0)
                list.Add(s);
        }
        return list;
    }

}
