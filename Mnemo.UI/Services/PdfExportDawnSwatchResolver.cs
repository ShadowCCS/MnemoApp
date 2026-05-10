using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Platform;

namespace Mnemo.UI.Services;

/// <summary>
/// PDF pages are always light; inline <c>swatch1</c>…<c>swatch10</c> backgrounds use the Dawn palette
/// so Dusk/Noon editor themes do not paint dark swatches on white paper.
/// </summary>
internal static class PdfExportDawnSwatchResolver
{
    private static IReadOnlyDictionary<string, string>? _cache;

    /// <summary>Fallback if <c>Colors.axaml</c> cannot be read; must stay in sync with Dawn <c>ColorSwatch1</c>–<c>10</c>.</summary>
    private static readonly IReadOnlyDictionary<string, string> Fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["swatch1"] = "#F5F5F5",
        ["swatch2"] = "#E6E6FA",
        ["swatch3"] = "#D8DCEC",
        ["swatch4"] = "#C4B5FD",
        ["swatch5"] = "#FADBD8",
        ["swatch6"] = "#E8F5E9",
        ["swatch7"] = "#FFF3CD",
        ["swatch8"] = "#FFE0B2",
        ["swatch9"] = "#DBEAFE",
        ["swatch10"] = "#D1EDDA"
    };

    public static IReadOnlyDictionary<string, string> GetBackgroundSwatchHexByName()
    {
        if (_cache != null)
            return _cache;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var uri = new Uri("avares://Mnemo.UI/Themes/Core/Dawn/Colors.axaml");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var xml = reader.ReadToEnd();
            foreach (Match m in Regex.Matches(
                         xml,
                         """<Color x:Key="ColorSwatch(\d{1,2})">#([0-9A-Fa-f]{6})</Color>""",
                         RegexOptions.IgnoreCase))
            {
                map["swatch" + m.Groups[1].Value] = "#" + m.Groups[2].Value.ToUpperInvariant();
            }
        }
        catch
        {
            // ignored
        }

        _cache = map.Count >= 10 ? map : new Dictionary<string, string>(Fallback, StringComparer.OrdinalIgnoreCase);
        return _cache;
    }
}
