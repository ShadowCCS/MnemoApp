using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MnemoApp.Core.Services
{
    public class SidebarService : ISidebarService
    {
        private readonly ObservableCollection<SidebarCategory> _categories;

        public ObservableCollection<SidebarCategory> Categories => _categories;

        public event Action<SidebarCategory>? CategoryAdded;
        public event Action<SidebarItem, SidebarCategory>? ItemAdded;
        public event Action<SidebarItem, SidebarCategory>? ItemRemoved;

        public SidebarService()
        {
            _categories = new ObservableCollection<SidebarCategory>();
        }

        public void Register(string title, Type viewModelType, string categoryName, string iconPath = "")
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title cannot be null or empty", nameof(title));
            
            if (viewModelType == null)
                throw new ArgumentNullException(nameof(viewModelType));
            
            if (string.IsNullOrWhiteSpace(categoryName))
                throw new ArgumentException("Category name cannot be null or empty", nameof(categoryName));

            // Find or create category
            var category = GetCategory(categoryName);
            if (category == null)
            {
                category = new SidebarCategory(categoryName);
                _categories.Add(category);
                CategoryAdded?.Invoke(category);
            }

            // Check if item already exists
            var existingItem = category.Items.FirstOrDefault(item => 
                item.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            
            if (existingItem != null)
            {
                // Update existing item
                existingItem.ViewModelType = viewModelType;
                if (!string.IsNullOrEmpty(iconPath))
                    existingItem.IconPath = iconPath;
            }
            else
            {
                // Create new item
                var newItem = new SidebarItem(title, viewModelType, iconPath);
                category.Items.Add(newItem);
                ItemAdded?.Invoke(newItem, category);
            }
        }

        public void Unregister(string title, string categoryName)
        {
            var category = GetCategory(categoryName);
            if (category == null) return;

            var item = category.Items.FirstOrDefault(item => 
                item.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            
            if (item != null)
            {
                category.Items.Remove(item);
                ItemRemoved?.Invoke(item, category);

                // Remove category if it's empty
                if (category.Items.Count == 0)
                {
                    _categories.Remove(category);
                }
            }
        }

        public SidebarCategory? GetCategory(string categoryName)
        {
            return _categories.FirstOrDefault(cat => 
                cat.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
        }

        public SidebarItem? GetItem(string title, string categoryName)
        {
            var category = GetCategory(categoryName);
            return category?.Items.FirstOrDefault(item => 
                item.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        }
    }
}