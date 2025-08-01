using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MnemoApp.Core.Overlays;

namespace MnemoApp.UI.Components.Overlays;

public partial class OverlayContainer : UserControl
{
    public static readonly StyledProperty<OverlayOptions> OptionsProperty =
        AvaloniaProperty.Register<OverlayContainer, OverlayOptions>(nameof(Options), new OverlayOptions());

    public static readonly StyledProperty<OverlayViewModelWrapper> WrapperProperty =
        AvaloniaProperty.Register<OverlayContainer, OverlayViewModelWrapper>(nameof(Wrapper));

    public OverlayOptions Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public OverlayViewModelWrapper Wrapper
    {
        get => GetValue(WrapperProperty);
        set => SetValue(WrapperProperty, value);
    }

    public event EventHandler<OverlayViewModelWrapper>? RequestClose;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == OptionsProperty)
        {
            ApplyOptions();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ApplyOptions();
        SetupEventHandlers();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Options?.CloseOnEscape == true)
        {
            CloseOverlay();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void SetupEventHandlers()
    {
        var backgroundGrid = this.FindControl<Grid>("BackgroundGrid");
        if (backgroundGrid != null)
        {
            backgroundGrid.PointerPressed += OnBackgroundPressed;
        }
        
        // Make sure this control can receive keyboard focus for escape key handling
        Focusable = true;
        IsTabStop = true;
    }

    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Options?.CloseOnBackgroundClick == true)
        {
            // Check if the click was on the background (not on content)
            var contentBorder = this.FindControl<Border>("ContentBorder");
            if (contentBorder != null)
            {
                var position = e.GetPosition(this);
                var contentBounds = contentBorder.Bounds;
                
                // If click is outside content area, close the overlay
                if (!contentBounds.Contains(position))
                {
                    CloseOverlay();
                    e.Handled = true;
                }
            }
        }
    }

    private void CloseOverlay()
    {
        if (Wrapper != null)
        {
            RequestClose?.Invoke(this, Wrapper);
        }
    }

    private void ApplyOptions()
    {
        var options = Options;
        if (options == null) return;
        
        // Find child controls by name
        var backgroundGrid = this.FindControl<Grid>("BackgroundGrid");
        var contentBorder = this.FindControl<Border>("ContentBorder");

        // Apply background overlay properties
        if (backgroundGrid != null)
        {
            if (options.ShowBackdrop)
            {
                backgroundGrid.Background = new SolidColorBrush(Color.FromArgb(
                    (byte)(options.BackgroundOpacity * 255),
                    options.BackgroundColor.R,
                    options.BackgroundColor.G,
                    options.BackgroundColor.B));
            }
            else
            {
                backgroundGrid.Background = Brushes.Transparent;
            }
            
            backgroundGrid.IsHitTestVisible = options.IsBackgroundClickable;
        }

        // Apply content container properties
        if (contentBorder != null)
        {
            contentBorder.HorizontalAlignment = options.HorizontalAlignment;
            contentBorder.VerticalAlignment = options.VerticalAlignment;
            contentBorder.Background = new SolidColorBrush(options.ContentBackgroundColor);
            contentBorder.CornerRadius = new CornerRadius(options.CornerRadius);
            contentBorder.Padding = new Thickness(options.ContentPadding);
            contentBorder.MinWidth = options.MinWidth;
            contentBorder.MinHeight = options.MinHeight;
            contentBorder.MaxWidth = options.MaxWidth;
            contentBorder.MaxHeight = options.MaxHeight;
            
            // Apply explicit width/height if specified
            if (options.Width.HasValue)
                contentBorder.Width = options.Width.Value;
            if (options.Height.HasValue)
                contentBorder.Height = options.Height.Value;
        }
    }

    public OverlayContainer()
    {
        InitializeComponent();
    }
}