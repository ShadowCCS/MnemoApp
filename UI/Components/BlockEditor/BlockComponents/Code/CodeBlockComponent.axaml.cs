using Avalonia.Controls;
using MnemoApp.UI.Components.BlockEditor;

namespace MnemoApp.UI.Components.BlockEditor.BlockComponents.Code;

public partial class CodeBlockComponent : BlockComponentBase
{
    public CodeBlockComponent()
    {
        InitializeComponent();
        InputTextBox.GotFocus += OnTextBoxGotFocus;
        InputTextBox.LostFocus += OnTextBoxLostFocus;
        InputTextBox.TextChanged += OnTextBoxTextChanged;
        InputTextBox.KeyDown += OnTextBoxKeyDown;
    }

    public override Control? GetInputControl() => InputTextBox;
}

