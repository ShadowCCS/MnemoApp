using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Checklist;

public partial class ChecklistBlockComponent : BlockComponentBase
{
    public ChecklistBlockComponent()
    {
        InitializeComponent();
        WireInputControl(InputTextBox);
    }

    public override Control? GetInputControl() => InputTextBox;
}


