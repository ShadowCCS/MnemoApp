using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Heading2;

public partial class Heading2BlockComponent : BlockComponentBase
{
    public Heading2BlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
