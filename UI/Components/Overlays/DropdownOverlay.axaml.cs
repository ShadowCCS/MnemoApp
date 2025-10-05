using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;
using MnemoApp.Core.Models;
using MnemoApp.Core.Services;

namespace MnemoApp.UI.Components.Overlays
{
    public partial class DropdownOverlay : UserControl
    {
        public DropdownOverlay()
        {
            InitializeComponent();
        }

        public void SetItems(IEnumerable<DropdownItemBase> items, Control? anchorControl = null)
        {
            DataContext = new DropdownOverlayViewModel(items, anchorControl);
        }

        public void SetItemsFromRegistry(DropdownType dropdownType, Control? anchorControl = null, string? category = null, IDropdownItemRegistry? registry = null)
        {
            registry ??= Core.ApplicationHost.GetServiceProvider().GetService(typeof(IDropdownItemRegistry)) as IDropdownItemRegistry;
            if (registry == null) return;

            var items = string.IsNullOrEmpty(category) 
                ? registry.GetItems(dropdownType)
                : registry.GetItems(dropdownType, category);
                
            SetItems(items, anchorControl);
        }
    }
}
