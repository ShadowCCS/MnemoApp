using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Heading1;

public partial class Heading1BlockComponent : BlockComponentBase
{
    public Heading1BlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
