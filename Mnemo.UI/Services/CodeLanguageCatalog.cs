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
    /// <summary>Most-used first; anything not listed sorts alphabetically after.</summary>
    private static readonly string[] PopularityOrder =
    [
        "sketch",
        "python",
        "javascript",
        "typescript",
        "tsx",
        "jsx",
        "csharp",
        "json",
        "html",
        "css",
        "scss",
        "less",
        "markdown",
        "yaml",
        "yml",
        "xml",
        "sql",
        "graphql",
        "rust",
        "go",
        "cpp",
        "cxx",
        "cc",
        "c",
        "h",
        "hpp",
        "shellscript",
        "bash",
        "zsh",
        "powershell",
        "dockerfile",
        "java",
        "kotlin",
        "swift",
        "ruby",
        "php",
        "toml",
        "ini",
    ];

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

            if (!map.ContainsKey("sketch"))
                map["sketch"] = "Sketch syntax";

            _cached = SortByPopularity(map.Select(kv => new CodeLanguageItem(kv.Key, kv.Value)).ToList());
        }
        catch
        {
            var fb = BuiltInFallback.ToList();
            if (!fb.Exists(x => x.Id.Equals("sketch", StringComparison.OrdinalIgnoreCase)))
                fb.Insert(0, new CodeLanguageItem("sketch", "Sketch syntax"));
            _cached = SortByPopularity(fb);
        }

        return _cached;
    }

    private static int PopularityBucket(string id)
    {
        for (var i = 0; i < PopularityOrder.Length; i++)
        {
            if (id.Equals(PopularityOrder[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return PopularityOrder.Length;
    }

    private static List<CodeLanguageItem> SortByPopularity(List<CodeLanguageItem> items) =>
        items
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => PopularityBucket(x.Id))
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
