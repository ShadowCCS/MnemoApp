using Avalonia.Controls;
using MnemoApp.UI.Components.BlockEditor;

namespace MnemoApp.UI.Components.BlockEditor.BlockComponents.Divider;

public partial class DividerBlockComponent : BlockComponentBase
{
    public DividerBlockComponent()
    {
        InitializeComponent();
    }

    public override Control? GetInputControl() => null;
}

