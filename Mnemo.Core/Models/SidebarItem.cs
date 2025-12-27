using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemo.Core.Models;

public partial class SidebarItem : ObservableObject
{
    public string Label { get; }
    public string Route { get; }
    public string Icon { get; }
    public string Title { get => Label; }
    public string IconPath { get => Icon; }
    
    [ObservableProperty]
    private bool _isSelected;

    public SidebarItem(string label, string route, string icon)
    {
        Label = label;
        Route = route;
        Icon = icon;
    }
}

public class SidebarCategory
{
    public string Name { get; }
    public ObservableCollection<SidebarItem> Items { get; } = new();

    public SidebarCategory(string name)
    {
        Name = name;
    }
}
