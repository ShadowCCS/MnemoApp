using Mnemo.Core.Formatting;

namespace Mnemo.UI.Services;

/// <summary>Applies rich-text keybind actions to the currently focused editor (notes block or RichDocumentEditor).</summary>
public interface IEditorKeybindDispatch
{
    void Apply(InlineFormatKind kind);
}
