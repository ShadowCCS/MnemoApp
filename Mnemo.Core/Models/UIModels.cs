using System.Windows.Input;

namespace Mnemo.Core.Models;

public class TopbarButtonModel
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string IconPath { get => Icon; set => Icon = value; }
    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; }
    public bool? Notification { get; set; }
    public string? ToolTip { get; set; }
}

