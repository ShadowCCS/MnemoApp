using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Quote;

public partial class QuoteBlockComponent : BlockComponentBase
{
    public QuoteBlockComponent()
    {
        InitializeComponent();
        WireRichTextEditor(Editor);
    }

    public override Control? GetInputControl() => Editor;
}
