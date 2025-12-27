namespace Mnemo.Core.Models;

public class BreadcrumbItem
{
    public string Title { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public bool IsLast { get; set; }
}

