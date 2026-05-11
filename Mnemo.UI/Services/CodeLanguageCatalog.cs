using System;
using System.Collections.Generic;
using System.Linq;
using TextMateSharp.Grammars;

namespace Mnemo.UI.Services;

/// <summary>VS Code language id + label for the note editor code block picker.</summary>
public sealed record CodeLanguageItem(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Languages exposed by bundled TextMateSharp.Grammars packages.</summary>
public static class CodeLanguageCatalog
{
    private static IReadOnlyList<CodeLanguageItem>? _cached;

    public static IReadOnlyList<CodeLanguageItem> GetLanguages()
    {
        if (_cached != null)
            return _cached;

        try
        {
            var opts = new RegistryOptions(ThemeName.DarkPlus);
            var list = opts.GetAvailableLanguages();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (list != null)
            {
                foreach (var o in list)
                {
                    if (o is not Language lang || string.IsNullOrWhiteSpace(lang.Id))
                        continue;
                    var label = lang.ToString();
                    map[lang.Id] = string.IsNullOrWhiteSpace(label) ? lang.Id : label;
                }
            }

            _cached = map
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new CodeLanguageItem(kv.Key, kv.Value))
                .ToArray();
        }
        catch
        {
            _cached = BuiltInFallback;
        }

        return _cached;
    }

    private static readonly CodeLanguageItem[] BuiltInFallback =
    [
        new("csharp", "C#"),
        new("javascript", "JavaScript"),
        new("typescript", "TypeScript"),
        new("python", "Python"),
        new("json", "JSON"),
        new("html", "HTML"),
        new("css", "CSS"),
        new("markdown", "Markdown"),
        new("yaml", "YAML"),
        new("sql", "SQL"),
        new("rust", "Rust"),
        new("go", "Go"),
        new("cpp", "C++"),
        new("c", "C"),
        new("shellscript", "Shell"),
        new("powershell", "PowerShell"),
        new("xml", "XML"),
    ];
}
