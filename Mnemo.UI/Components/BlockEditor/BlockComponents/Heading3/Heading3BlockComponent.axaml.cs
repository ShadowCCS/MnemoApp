using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Heading3;

public partial class Heading3BlockComponent : BlockComponentBase
{
    public Heading3BlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
