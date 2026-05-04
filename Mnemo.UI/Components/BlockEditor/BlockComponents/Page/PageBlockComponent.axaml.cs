using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using EditorHost = Mnemo.UI.Components.BlockEditor.BlockEditor;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents.Page;

public partial class PageBlockComponent : BlockComponentBase
{
    public PageBlockComponent()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public override Control? GetInputControl() => null;

    private void OpenPageButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel == null || string.IsNullOrWhiteSpace(ViewModel.ReferenceNoteId))
            return;
        var editor = this.GetVisualAncestors().OfType<EditorHost>().FirstOrDefault();
        editor?.RequestOpenReferencedNote(ViewModel.ReferenceNoteId);
    }
}
