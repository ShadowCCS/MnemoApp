using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemo.Core.Models;

public partial class SidebarItem : ObservableObject
{
    [ObservableProperty]
    private string _label;

    public string Route { get; }
    public string Icon { get; }
    public string Title => Label;
    public string IconPath => Icon;
    public int Order { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SidebarItem(string label, string route, string icon, int order = int.MaxValue)
    {
        _label = label;
        Route = route;
        Icon = icon;
        Order = order;
    }
}

public partial class SidebarCategory : ObservableObject
{
    [ObservableProperty]
    private string _name;

    public int Order { get; }
    public ObservableCollection<SidebarItem> Items { get; } = new();

    public SidebarCategory(string name, int order = int.MaxValue)
    {
        _name = name;
        Order = order;
    }
}
