using Avalonia.Controls;
using MnemoApp.UI.Components.BlockEditor;

namespace MnemoApp.UI.Components.BlockEditor.BlockComponents.Heading2;

public partial class Heading2BlockComponent : BlockComponentBase
{
    public Heading2BlockComponent()
    {
        InitializeComponent();
        InputTextBox.GotFocus += OnTextBoxGotFocus;
        InputTextBox.LostFocus += OnTextBoxLostFocus;
        InputTextBox.TextChanged += OnTextBoxTextChanged;
        InputTextBox.KeyDown += OnTextBoxKeyDown;
    }

    public override Control? GetInputControl() => InputTextBox;
}

