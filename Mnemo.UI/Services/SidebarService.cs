using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class SidebarService : ISidebarService, INotifyPropertyChanged
{
    private bool _isCollapsed;

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

    public void RegisterItem(string label, string route, string icon, string categoryName = "General")
    {
        var category = Categories.FirstOrDefault(c => c.Name == categoryName);
        if (category == null)
        {
            category = new SidebarCategory(categoryName);
            Categories.Add(category);
        }
        
        category.Items.Add(new SidebarItem(label, route, icon));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

