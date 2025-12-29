using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

public enum AnchorPosition
{
    TopLeft, 
    TopRight, 
    BottomLeft, 
    BottomRight, 
    TopCenter, 
    BottomCenter, 
    LeftCenter, 
    RightCenter
}

public class OverlayOptions
{
    public object? AnchorControl { get; set; } 
    public AnchorPosition AnchorPosition { get; set; }
    public object? AnchorOffset { get; set; } 
    public object Margin { get; set; } = "0";
    public bool CloseOnOutsideClick { get; set; } = true;
    public bool ShowBackdrop { get; set; } = true;
    public object HorizontalAlignment { get; set; } = "Center";
    public object VerticalAlignment { get; set; } = "Center";
    public object? BackdropBrush { get; set; }
    public string? BackdropColor { get; set; }
    public double BackdropOpacity { get; set; } = 0.5;
}

public class OverlayInstance : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public object? Content { get; set; }
    public OverlayOptions Options { get; set; } = new();
    public int ZIndex { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public interface IOverlayService
{
    ObservableCollection<OverlayInstance> Overlays { get; }
    void Show(string overlayName, object? parameter = null);
    void Hide();
    void CloseOverlay(string id);
    void CloseOverlay(string id, object? result);
    string CreateOverlay(object content, OverlayOptions options, string? name = null);
    Task<string?> CreateDialogAsync(string title, string message, string confirmText = "OK", string cancelText = "", object? icon = null, object? parameter = null);
}
