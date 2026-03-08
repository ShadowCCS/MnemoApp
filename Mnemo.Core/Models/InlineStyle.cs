namespace Mnemo.Core.Models;

/// <summary>
/// Immutable annotation set for an inline run, mirroring Notion's annotation object.
/// Value equality enables adjacent-run merging.
/// </summary>
public readonly record struct InlineStyle(
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false,
    bool Code = false,
    string? BackgroundColor = null)
{
    public static readonly InlineStyle Default = new();

    /// <summary>Returns a copy with a single flag toggled.</summary>
    public InlineStyle WithToggle(Formatting.InlineFormatKind kind, string? color = null) => kind switch
    {
        Formatting.InlineFormatKind.Bold => this with { Bold = !Bold },
        Formatting.InlineFormatKind.Italic => this with { Italic = !Italic },
        Formatting.InlineFormatKind.Underline => this with { Underline = !Underline },
        Formatting.InlineFormatKind.Strikethrough => this with { Strikethrough = !Strikethrough },
        Formatting.InlineFormatKind.Code => this with { Code = !Code },
        Formatting.InlineFormatKind.Highlight => this with { BackgroundColor = BackgroundColor != null ? null : (color ?? "#FFFF00") },
        Formatting.InlineFormatKind.BackgroundColor => this with { BackgroundColor = BackgroundColor == color ? null : color },
        _ => this
    };

    /// <summary>Returns a copy with a single flag forced on.</summary>
    public InlineStyle WithSet(Formatting.InlineFormatKind kind, string? color = null) => kind switch
    {
        Formatting.InlineFormatKind.Bold => this with { Bold = true },
        Formatting.InlineFormatKind.Italic => this with { Italic = true },
        Formatting.InlineFormatKind.Underline => this with { Underline = true },
        Formatting.InlineFormatKind.Strikethrough => this with { Strikethrough = true },
        Formatting.InlineFormatKind.Code => this with { Code = true },
        Formatting.InlineFormatKind.Highlight => this with { BackgroundColor = color ?? "#FFFF00" },
        Formatting.InlineFormatKind.BackgroundColor => this with { BackgroundColor = color },
        _ => this
    };

    /// <summary>Returns a copy with a single flag forced off.</summary>
    public InlineStyle WithClear(Formatting.InlineFormatKind kind) => kind switch
    {
        Formatting.InlineFormatKind.Bold => this with { Bold = false },
        Formatting.InlineFormatKind.Italic => this with { Italic = false },
        Formatting.InlineFormatKind.Underline => this with { Underline = false },
        Formatting.InlineFormatKind.Strikethrough => this with { Strikethrough = false },
        Formatting.InlineFormatKind.Code => this with { Code = false },
        Formatting.InlineFormatKind.Highlight => this with { BackgroundColor = null },
        Formatting.InlineFormatKind.BackgroundColor => this with { BackgroundColor = null },
        _ => this
    };

    /// <summary>Checks whether a specific format flag is active.</summary>
    public bool Has(Formatting.InlineFormatKind kind) => kind switch
    {
        Formatting.InlineFormatKind.Bold => Bold,
        Formatting.InlineFormatKind.Italic => Italic,
        Formatting.InlineFormatKind.Underline => Underline,
        Formatting.InlineFormatKind.Strikethrough => Strikethrough,
        Formatting.InlineFormatKind.Code => Code,
        Formatting.InlineFormatKind.Highlight => BackgroundColor != null,
        Formatting.InlineFormatKind.BackgroundColor => BackgroundColor != null,
        _ => false
    };
}
