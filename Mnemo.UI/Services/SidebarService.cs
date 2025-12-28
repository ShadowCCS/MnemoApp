using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class SidebarService : ISidebarService, INotifyPropertyChanged
{
    private bool _isCollapsed;

    private static readonly Dictionary<string, int> DefaultCategoryOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Main hub", 0 },
        { "Library", 1 },
        { "Ecosystem", 2 }
    };

    public ObservableCollection<SidebarCategory> Categories { get; } = new();

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed != value)
            {
                _isCollapsed = value;
                OnPropertyChanged();
            }
        }
    }

    public void RegisterItem(string label, string route, string icon, string categoryName = "General", int? categoryOrder = null, int itemOrder = int.MaxValue)
    {
        var category = Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
        if (category == null)
        {
            var order = categoryOrder ?? 
                       (DefaultCategoryOrder.TryGetValue(categoryName, out var defaultOrder) ? defaultOrder : int.MaxValue);
            category = new SidebarCategory(categoryName, order);
            
            // Insert at the correct position to maintain order
            var insertIndex = Categories.Count;
            for (int i = 0; i < Categories.Count; i++)
            {
                if (Categories[i].Order > order)
                {
                    insertIndex = i;
                    break;
                }
            }
            Categories.Insert(insertIndex, category);
        }
        
        var item = new SidebarItem(label, route, icon, itemOrder);
        
        // Insert at the correct position to maintain order
        var itemInsertIndex = category.Items.Count;
        for (int i = 0; i < category.Items.Count; i++)
        {
            if (category.Items[i].Order > itemOrder)
            {
                itemInsertIndex = i;
                break;
            }
        }
        category.Items.Insert(itemInsertIndex, item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

