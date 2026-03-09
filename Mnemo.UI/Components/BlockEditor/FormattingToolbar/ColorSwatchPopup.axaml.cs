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
    private static readonly string[] SwatchColorKeys =
    {
        "ColorSwatch1", "ColorSwatch2", "ColorSwatch3", "ColorSwatch4",
        "ColorSwatch5", "ColorSwatch6", "ColorSwatch7", "ColorSwatch8",
        "ColorSwatch9", "ColorSwatch10"
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

        var app = Application.Current;
        for (int i = 0; i < SwatchColorKeys.Length; i++)
        {
            var key = SwatchColorKeys[i];
            var swatchName = "swatch" + (i + 1);
            if (app != null && app.TryFindResource(key, out var res) && res is Color color)
            {
                var ellipse = new Ellipse
                {
                    Width = 28,
                    Height = 28,
                    Fill = new SolidColorBrush(color),
                    Margin = new Thickness(3),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                ellipse.PointerPressed += (s, args) =>
                {
                    _selectedColor = swatchName;
                    ColorSelected?.Invoke(swatchName);
                    args.Handled = true;
                };

                panel.Children.Add(ellipse);
            }
        }

        Loaded -= OnLoaded;
    }
}
