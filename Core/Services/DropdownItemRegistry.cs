using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MnemoApp.Core.Models;

namespace MnemoApp.Core.Services
{
    public class DropdownItemRegistry : IDropdownItemRegistry
    {
        private readonly ConcurrentDictionary<DropdownType, List<DropdownItemBase>> _items = new();
        
        public event Action<DropdownType>? ItemsChanged;

        public void RegisterItem(DropdownType dropdownType, DropdownItemBase item)
        {
            var items = _items.GetOrAdd(dropdownType, _ => new List<DropdownItemBase>());
            
            lock (items)
            {
                // Remove existing item with same ID if it exists
                if (!string.IsNullOrEmpty(item.Id))
                {
                    items.RemoveAll(i => i.Id == item.Id);
                }
                
                items.Add(item);
                items.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
            
            ItemsChanged?.Invoke(dropdownType);
        }

        public void RegisterItems(DropdownType dropdownType, IEnumerable<DropdownItemBase> items)
        {
            var itemList = _items.GetOrAdd(dropdownType, _ => new List<DropdownItemBase>());
            
            lock (itemList)
            {
                foreach (var item in items)
                {
                    // Remove existing item with same ID if it exists
                    if (!string.IsNullOrEmpty(item.Id))
                    {
                        itemList.RemoveAll(i => i.Id == item.Id);
                    }
                    itemList.Add(item);
                }
                itemList.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
            
            ItemsChanged?.Invoke(dropdownType);
        }

        public IEnumerable<DropdownItemBase> GetItems(DropdownType dropdownType)
        {
            if (!_items.TryGetValue(dropdownType, out var items))
                return Enumerable.Empty<DropdownItemBase>();
                
            lock (items)
            {
                return items.ToList(); // Return copy to avoid mutation issues
            }
        }

        public IEnumerable<DropdownItemBase> GetItems(DropdownType dropdownType, string category)
        {
            return GetItems(dropdownType)
                .Where(item => item is DropdownOption option && option.Category == category);
        }

        public bool RemoveItem(DropdownType dropdownType, string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !_items.TryGetValue(dropdownType, out var items))
                return false;
                
            lock (items)
            {
                var removed = items.RemoveAll(i => i.Id == itemId) > 0;
                if (removed)
                {
                    ItemsChanged?.Invoke(dropdownType);
                }
                return removed;
            }
        }

        public void ClearItems(DropdownType dropdownType)
        {
            if (_items.TryGetValue(dropdownType, out var items))
            {
                lock (items)
                {
                    items.Clear();
                }
                ItemsChanged?.Invoke(dropdownType);
            }
        }

        public bool HasItem(DropdownType dropdownType, string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !_items.TryGetValue(dropdownType, out var items))
                return false;
                
            lock (items)
            {
                return items.Any(i => i.Id == itemId);
            }
        }
    }
}
