using System;
using System.Collections.ObjectModel;

namespace MnemoApp.Core.Services
{
    public interface ISidebarService
    {
        ObservableCollection<SidebarCategory> Categories { get; }
        
        void Register(string title, Type viewModelType, string categoryName, string iconPath = "");
        void Unregister(string title, string categoryName);
        SidebarCategory? GetCategory(string categoryName);
        SidebarItem? GetItem(string title, string categoryName);
        
        event Action<SidebarCategory>? CategoryAdded;
        event Action<SidebarItem, SidebarCategory>? ItemAdded;
        event Action<SidebarItem, SidebarCategory>? ItemRemoved;
    }
}