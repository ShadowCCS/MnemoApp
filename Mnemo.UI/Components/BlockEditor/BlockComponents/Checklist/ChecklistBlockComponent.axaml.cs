using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Checklist;

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


