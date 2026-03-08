using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.NumberedList;

public partial class NumberedListBlockComponent : BlockComponentBase
{
    public NumberedListBlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
