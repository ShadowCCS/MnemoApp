using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Quote;

public partial class QuoteBlockComponent : BlockComponentBase
{
    public QuoteBlockComponent()
    {
        InitializeComponent();
        WireInputControl(InputTextBox);
    }

    public override Control? GetInputControl() => InputTextBox;
}


