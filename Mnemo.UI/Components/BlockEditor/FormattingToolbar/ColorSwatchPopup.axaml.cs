using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Mnemo.Core.Services;
using System;

namespace Mnemo.UI.Components.BlockEditor.FormattingToolbar;

public partial class ColorSwatchPopup : UserControl
{
    private static readonly string[] SwatchColors =
    {
        "#E0E0E0", "#64B5F6", "#42A5F5", "#7C5CFC", "#EF5350",
        "#66BB6A", "#FF9800", "#FF5722", "#29B6F6", "#26A69A"
    };

    private string? _selectedColor;

    public event Action<string>? ColorSelected;

    public ColorSwatchPopup()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<WrapPanel>("SwatchPanel");
        if (panel == null) return;

        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        var headerLabel = this.FindControl<TextBlock>("HeaderLabel");
        if (headerLabel != null && loc != null)
            headerLabel.Text = loc.T("TextColor", "NotesEditor") ?? "TEXT COLOR";

        foreach (var hex in SwatchColors)
        {
            var ellipse = new Ellipse
            {
                Width = 28,
                Height = 28,
                Fill = new SolidColorBrush(Color.Parse(hex)),
                Margin = new Thickness(3),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            ellipse.PointerPressed += (s, args) =>
            {
                _selectedColor = hex;
                ColorSelected?.Invoke(hex);
                args.Handled = true;
            };

            panel.Children.Add(ellipse);
        }

        Loaded -= OnLoaded;
    }
}
