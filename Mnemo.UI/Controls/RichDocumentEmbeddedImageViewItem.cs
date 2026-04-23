namespace Mnemo.UI.Controls;

/// <summary>
/// View model item for images embedded in <see cref="RichDocumentEditor"/> text.
/// </summary>
public sealed class RichDocumentEmbeddedImageViewItem
{
    public required int Index { get; init; }
    public required string Path { get; init; }
    public required string Align { get; init; }

    public bool IsAlignLeft => string.Equals(Align, "left", StringComparison.Ordinal);
    public bool IsAlignCenter => string.Equals(Align, "center", StringComparison.Ordinal);
    public bool IsAlignRight => string.Equals(Align, "right", StringComparison.Ordinal);
}
