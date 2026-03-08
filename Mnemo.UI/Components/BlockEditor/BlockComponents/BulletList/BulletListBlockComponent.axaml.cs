using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.BulletList;

public partial class BulletListBlockComponent : BlockComponentBase
{
    public BulletListBlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
