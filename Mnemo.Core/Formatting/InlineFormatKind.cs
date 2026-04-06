namespace Mnemo.Core.Formatting;

public enum InlineFormatKind
{
    Bold,
    Italic,
    Underline,
    Strikethrough,
    Highlight,
    BackgroundColor,
    Code,
    Subscript,
    Superscript,
    /// <summary>Hyperlink; set URL via apply with string parameter, or null to remove.</summary>
    Link
}
