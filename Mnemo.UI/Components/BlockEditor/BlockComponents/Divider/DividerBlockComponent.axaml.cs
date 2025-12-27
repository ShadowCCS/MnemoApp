using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Divider;

public partial class DividerBlockComponent : BlockComponentBase
{
    public DividerBlockComponent()
    {
        InitializeComponent();
    }

    public override Control? GetInputControl() => null;
}


