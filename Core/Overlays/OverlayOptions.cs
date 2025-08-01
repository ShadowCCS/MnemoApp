using Avalonia.Layout;
using Avalonia.Media;

namespace MnemoApp.Core.Overlays;

public class OverlayOptions
{
    // Modal behavior
    public bool IsModal { get; set; } = true; // If true, the overlay will be modal and will block interaction with the rest of the app
    public bool CloseOnBackgroundClick { get; set; } = true; // If true, the overlay will be closed when the background is clicked
    public bool CloseOnEscape { get; set; } = true; // If true, the overlay will be closed when the escape key is pressed
    public bool ShowBackdrop { get; set; } = true; // If true, the overlay will have a backdrop
    public bool AutoCloseOnNavigation { get; set; } = true; // If true, the overlay will be closed when the navigation is triggered
    
    // Positioning and Size
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Center;
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Center;
    public double? Width { get; set; } // If set, the overlay will have a width
    public double? Height { get; set; } // If set, the overlay will have a height
    public double MinWidth { get; set; } = 0;
    public double MinHeight { get; set; } = 0;
    public double MaxWidth { get; set; } = double.PositiveInfinity;
    public double MaxHeight { get; set; } = double.PositiveInfinity;
    
    // Styling
    public Color BackgroundColor { get; set; } = Colors.Black; // Background overlay color
    public double BackgroundOpacity { get; set; } = 0.5; // 0.0-1.0
    public Color ContentBackgroundColor { get; set; } = Colors.White; // Content background color
    public double CornerRadius { get; set; } = 8;
    public double ContentPadding { get; set; } = 0;
    public bool IsBackgroundClickable { get; set; } = true; // Whether background clicks should be handled
    
    // Legacy properties for backward compatibility
    public string? Placement { get; set; } = "Center"; // center, left, right, x,y, top, bottom
    public int? Opacity { get; set; } = 100; // 0-100 (converted to BackgroundOpacity)
    public int? ZIndex { get; set; } = 1000; // 0-1000
    
    public OverlayOptions()
    {
        // Convert legacy Opacity to BackgroundOpacity if set
        if (Opacity.HasValue)
        {
            BackgroundOpacity = Opacity.Value / 100.0;
        }
        
        // Convert legacy Placement to alignment
        UpdateAlignmentFromPlacement();
    }
    
    private void UpdateAlignmentFromPlacement()
    {
        if (string.IsNullOrEmpty(Placement)) return;
        
        switch (Placement.ToLower())
        {
            case "center":
                HorizontalAlignment = HorizontalAlignment.Center;
                VerticalAlignment = VerticalAlignment.Center;
                break;
            case "left":
                HorizontalAlignment = HorizontalAlignment.Left;
                VerticalAlignment = VerticalAlignment.Center;
                break;
            case "right":
                HorizontalAlignment = HorizontalAlignment.Right;
                VerticalAlignment = VerticalAlignment.Center;
                break;
            case "top":
                HorizontalAlignment = HorizontalAlignment.Center;
                VerticalAlignment = VerticalAlignment.Top;
                break;
            case "bottom":
                HorizontalAlignment = HorizontalAlignment.Center;
                VerticalAlignment = VerticalAlignment.Bottom;
                break;
        }
    }
} 