using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.Overlays
{
    public partial class DropdownOverlayViewModel : ObservableObject
    {
        [ObservableProperty]
        private IEnumerable<DropdownItemBase> _items = new List<DropdownItemBase>();

        [ObservableProperty]
        private Control? _anchorControl;

        [ObservableProperty]
        private Thickness _margin = new Thickness(0);

        [ObservableProperty]
        private double _offsetX = 0;

        [ObservableProperty]
        private double _offsetY = 0;

        public DropdownOverlayViewModel(IEnumerable<DropdownItemBase> items, Control? anchorControl = null)
        {
            Items = items;
            AnchorControl = anchorControl;
            
            if (anchorControl != null)
            {
                UpdatePosition();
            }
        }

        public void UpdatePosition()
        {
            if (AnchorControl == null) return;

            // Find the top-level window or visual root
            var topLevel = AnchorControl.FindAncestorOfType<TopLevel>();
            if (topLevel == null) return;

            try
            {
                // Get the anchor control's bounds relative to the top level
                var anchorBounds = AnchorControl.TranslatePoint(new Point(0, 0), topLevel);
                if (!anchorBounds.HasValue) return;

                // Position dropdown slightly to the right and below the anchor
                var x = anchorBounds.Value.X + 8; // 8px to the right
                var y = anchorBounds.Value.Y + AnchorControl.Bounds.Height + 4; // 4px below

                OffsetX = x;
                OffsetY = y;
                Margin = new Thickness(x, y, 0, 0);
            }
            catch
            {
                // Fallback to default positioning
                Margin = new Thickness(0, 50, 0, 0);
            }
        }
    }
}

