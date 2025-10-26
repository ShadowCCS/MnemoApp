using Avalonia.Controls;
using Avalonia.Data;
using Avalonia;

namespace MnemoApp.UI.Components;

public partial class LocalSearchBar : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LocalSearchBar, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public LocalSearchBar()
    {
        InitializeComponent();
    }
}
