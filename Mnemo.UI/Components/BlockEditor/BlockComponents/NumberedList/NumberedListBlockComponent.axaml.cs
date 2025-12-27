using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.NumberedList;

public partial class NumberedListBlockComponent : BlockComponentBase
{
    public NumberedListBlockComponent()
    {
        InitializeComponent();
        InputTextBox.GotFocus += OnTextBoxGotFocus;
        InputTextBox.LostFocus += OnTextBoxLostFocus;
        InputTextBox.TextChanged += OnTextBoxTextChanged;
        InputTextBox.KeyDown += OnTextBoxKeyDown;
    }

    public override Control? GetInputControl() => InputTextBox;
}


