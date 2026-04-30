using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Heading4;

public partial class Heading4BlockComponent : BlockComponentBase
{
    public Heading4BlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
