using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Mnemo.Core.Services;

public enum AnchorPosition
{
    TopLeft, TopRight, BottomLeft, BottomRight, TopCenter, BottomCenter, LeftCenter, RightCenter
}

public class OverlayOptions
{
    public dynamic? AnchorControl { get; set; } 
    public AnchorPosition AnchorPosition { get; set; }
    public dynamic? AnchorOffset { get; set; } 
    public dynamic? Margin { get; set; }
    public bool CloseOnOutsideClick { get; set; } = true;
    public bool ShowBackdrop { get; set; } = true;
    public dynamic? HorizontalAlignment { get; set; }
    public dynamic? VerticalAlignment { get; set; }
    public dynamic? BackdropBrush { get; set; }
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
    string CreateOverlay(object content, OverlayOptions options, string name);
    System.Threading.Tasks.Task CreateDialog(string title, string message, string confirmText = "OK", string cancelText = "", object? icon = null, object? parameter = null);
}
