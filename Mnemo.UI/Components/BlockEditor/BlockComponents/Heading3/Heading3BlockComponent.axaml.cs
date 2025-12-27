using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Heading3;

public partial class Heading3BlockComponent : BlockComponentBase
{
    public Heading3BlockComponent()
    {
        InitializeComponent();
        InputTextBox.GotFocus += OnTextBoxGotFocus;
        InputTextBox.LostFocus += OnTextBoxLostFocus;
        InputTextBox.TextChanged += OnTextBoxTextChanged;
        InputTextBox.KeyDown += OnTextBoxKeyDown;
    }

    public override Control? GetInputControl() => InputTextBox;
}


