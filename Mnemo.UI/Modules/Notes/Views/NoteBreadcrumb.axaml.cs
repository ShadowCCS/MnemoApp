using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mnemo.UI.Modules.Notes.Views;

public partial class NoteBreadcrumb : UserControl
{
    public NoteBreadcrumb()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
