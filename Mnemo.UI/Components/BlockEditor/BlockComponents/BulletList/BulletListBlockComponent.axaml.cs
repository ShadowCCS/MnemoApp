using Avalonia.Controls;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.BulletList;

public partial class BulletListBlockComponent : BlockComponentBase
{
    public BulletListBlockComponent()
    {
        InitializeComponent();
        InputTextBox.GotFocus += OnTextBoxGotFocus;
        InputTextBox.LostFocus += OnTextBoxLostFocus;
        InputTextBox.TextChanged += OnTextBoxTextChanged;
        InputTextBox.KeyDown += OnTextBoxKeyDown;
    }

    public override Control? GetInputControl() => InputTextBox;
}


