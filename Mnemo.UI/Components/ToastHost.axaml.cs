using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Mnemo.UI.Services;

namespace Mnemo.UI.Components;

public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
    }

    public ToastHost(ToastService toastService) : this()
    {
        DataContext = toastService;
    }
}
