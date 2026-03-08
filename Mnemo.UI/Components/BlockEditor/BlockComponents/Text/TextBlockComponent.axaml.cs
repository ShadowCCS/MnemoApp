using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Text;

public partial class TextBlockComponent : BlockComponentBase
{
    public TextBlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
