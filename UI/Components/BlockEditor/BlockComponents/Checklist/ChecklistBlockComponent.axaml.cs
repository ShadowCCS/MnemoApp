using Avalonia.Controls;
using MnemoApp.UI.Components.BlockEditor;

namespace MnemoApp.UI.Components.BlockEditor.BlockComponents.Checklist;

public partial class ChecklistBlockComponent : BlockComponentBase
{
    public ChecklistBlockComponent()
    {
        InitializeComponent();
        InputTextBox.GotFocus += OnTextBoxGotFocus;
        InputTextBox.LostFocus += OnTextBoxLostFocus;
        InputTextBox.TextChanged += OnTextBoxTextChanged;
        InputTextBox.KeyDown += OnTextBoxKeyDown;
    }

    public override Control? GetInputControl() => InputTextBox;
}

