using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Heading2;

public partial class Heading2BlockComponent : BlockComponentBase
{
    public Heading2BlockComponent()
    {
        InitializeComponent();
        WireInputControl(InputTextBox);
    }

    public override Control? GetInputControl() => InputTextBox;
}


