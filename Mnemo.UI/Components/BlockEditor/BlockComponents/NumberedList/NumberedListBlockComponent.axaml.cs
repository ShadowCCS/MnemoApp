using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.NumberedList;

public partial class NumberedListBlockComponent : BlockComponentBase
{
    public NumberedListBlockComponent()
    {
        InitializeComponent();
        WireInputControl(InputTextBox);
    }

    public override Control? GetInputControl() => InputTextBox;
}


