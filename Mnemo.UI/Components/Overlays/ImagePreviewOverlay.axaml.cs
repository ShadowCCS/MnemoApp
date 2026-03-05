using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Mnemo.UI.Components.Overlays;

public partial class ImagePreviewOverlay : UserControl
{
    public static readonly StyledProperty<string?> ImagePathProperty =
        AvaloniaProperty.Register<ImagePreviewOverlay, string?>(nameof(ImagePath));

    public string? ImagePath
    {
        get => GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    /// <summary>Invoked when the user requests to close the overlay (backdrop click or close button).</summary>
    public Action? CloseRequested { get; set; }

    public ImagePreviewOverlay()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseRequested?.Invoke();
        e.Handled = true;
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
        e.Handled = true;
    }
}
