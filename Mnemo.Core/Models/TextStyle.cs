namespace Mnemo.Core.Models;

/// <summary>Text-only annotations (no inline equation — use <see cref="EquationSpan"/>).</summary>
public readonly record struct TextStyle(
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false,
    bool Code = false,
    string? BackgroundColor = null,
    string? LinkUrl = null,
    bool SuppressAutoLink = false)
{
    public static readonly TextStyle Default = new();

    public TextStyle WithToggle(Formatting.InlineFormatKind kind, string? color = null) => kind switch
    {
        Formatting.InlineFormatKind.Bold => this with { Bold = !Bold },
        Formatting.InlineFormatKind.Italic => this with { Italic = !Italic },
        Formatting.InlineFormatKind.Underline => this with { Underline = !Underline },
        Formatting.InlineFormatKind.Strikethrough => this with { Strikethrough = !Strikethrough },
        Formatting.InlineFormatKind.Code => this with { Code = !Code },
        Formatting.InlineFormatKind.Highlight => this with { BackgroundColor = BackgroundColor != null ? null : (color ?? "#FFFF00") },
        Formatting.InlineFormatKind.BackgroundColor => this with { BackgroundColor = BackgroundColor == color ? null : color },
        Formatting.InlineFormatKind.Link => LinkUrl != null
            ? this with { LinkUrl = null, SuppressAutoLink = true }
            : this,
        Formatting.InlineFormatKind.Equation => this,
        _ => this
    };

    public TextStyle WithSet(Formatting.InlineFormatKind kind, string? color = null) => kind switch
    {
        Formatting.InlineFormatKind.Bold => this with { Bold = true },
        Formatting.InlineFormatKind.Italic => this with { Italic = true },
        Formatting.InlineFormatKind.Underline => this with { Underline = true },
        Formatting.InlineFormatKind.Strikethrough => this with { Strikethrough = true },
        Formatting.InlineFormatKind.Code => this with { Code = true },
        Formatting.InlineFormatKind.Highlight => this with { BackgroundColor = color ?? "#FFFF00" },
        Formatting.InlineFormatKind.BackgroundColor => this with { BackgroundColor = color },
        Formatting.InlineFormatKind.Link => this with { LinkUrl = color, SuppressAutoLink = false },
        Formatting.InlineFormatKind.Equation => this,
        _ => this
    };

    public TextStyle WithClear(Formatting.InlineFormatKind kind) => kind switch
    {
        Formatting.InlineFormatKind.Bold => this with { Bold = false },
        Formatting.InlineFormatKind.Italic => this with { Italic = false },
        Formatting.InlineFormatKind.Underline => this with { Underline = false },
        Formatting.InlineFormatKind.Strikethrough => this with { Strikethrough = false },
        Formatting.InlineFormatKind.Code => this with { Code = false },
        Formatting.InlineFormatKind.Highlight => this with { BackgroundColor = null },
        Formatting.InlineFormatKind.BackgroundColor => this with { BackgroundColor = null },
        Formatting.InlineFormatKind.Link => LinkUrl != null
            ? this with { LinkUrl = null, SuppressAutoLink = true }
            : this,
        Formatting.InlineFormatKind.Equation => this,
        _ => this
    };

    public bool Has(Formatting.InlineFormatKind kind) => kind switch
    {
        Formatting.InlineFormatKind.Bold => Bold,
        Formatting.InlineFormatKind.Italic => Italic,
        Formatting.InlineFormatKind.Underline => Underline,
        Formatting.InlineFormatKind.Strikethrough => Strikethrough,
        Formatting.InlineFormatKind.Code => Code,
        Formatting.InlineFormatKind.Highlight => BackgroundColor != null,
        Formatting.InlineFormatKind.BackgroundColor => BackgroundColor != null,
        Formatting.InlineFormatKind.Link => LinkUrl != null,
        Formatting.InlineFormatKind.Equation => false,
        _ => false
    };
}
