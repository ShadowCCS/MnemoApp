using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Checklist;

public partial class ChecklistBlockComponent : BlockComponentBase
{
    public ChecklistBlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
