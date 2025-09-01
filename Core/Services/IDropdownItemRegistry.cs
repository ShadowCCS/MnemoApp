using System;
using System.Collections.Generic;
using MnemoApp.Core.Models;

namespace MnemoApp.Core.Services
{
    public interface IDropdownItemRegistry
    {
        /// <summary>
        /// Register a dropdown item for a specific dropdown type
        /// </summary>
        void RegisterItem(DropdownType dropdownType, DropdownItemBase item);
        
        /// <summary>
        /// Register multiple dropdown items for a specific dropdown type
        /// </summary>
        void RegisterItems(DropdownType dropdownType, IEnumerable<DropdownItemBase> items);
        
        /// <summary>
        /// Get all registered items for a specific dropdown type, ordered by Order property
        /// </summary>
        IEnumerable<DropdownItemBase> GetItems(DropdownType dropdownType);
        
        /// <summary>
        /// Get all registered items for a specific dropdown type and category
        /// </summary>
        IEnumerable<DropdownItemBase> GetItems(DropdownType dropdownType, string category);
        
        /// <summary>
        /// Remove a registered item by ID
        /// </summary>
        bool RemoveItem(DropdownType dropdownType, string itemId);
        
        /// <summary>
        /// Clear all items for a dropdown type
        /// </summary>
        void ClearItems(DropdownType dropdownType);
        
        /// <summary>
        /// Check if an item exists
        /// </summary>
        bool HasItem(DropdownType dropdownType, string itemId);
        
        /// <summary>
        /// Event fired when items are added/removed/modified
        /// </summary>
        event Action<DropdownType>? ItemsChanged;
    }
}
