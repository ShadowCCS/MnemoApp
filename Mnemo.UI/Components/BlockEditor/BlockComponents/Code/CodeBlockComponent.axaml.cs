using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Code;

public partial class CodeBlockComponent : BlockComponentBase
{
    public CodeBlockComponent()
    {
        InitializeComponent();
        WireInputControl(InputTextBox);
    }

    public override Control? GetInputControl() => InputTextBox;
}


