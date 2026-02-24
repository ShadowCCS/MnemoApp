using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Mixins;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Mnemo.UI.Controls
{
    /// <summary>
    /// An item in a <see cref="MnemoTreeView"/>. Expand/collapse is triggered by single click (no double-tap delay).
    /// </summary>
    [TemplatePart("PART_Header", typeof(Control))]
    [TemplatePart("PART_ExpandCollapseChevron", typeof(Control))]
    [PseudoClasses(":pressed", ":selected")]
    public class MnemoTreeViewItem : HeaderedItemsControl, ISelectable
    {
        /// <summary>
        /// Defines the <see cref="IsExpanded"/> property.
        /// </summary>
        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<MnemoTreeViewItem, bool>(
                nameof(IsExpanded),
                defaultBindingMode: BindingMode.TwoWay);

        /// <summary>
        /// Defines the <see cref="IsSelected"/> property.
        /// </summary>
        public static readonly StyledProperty<bool> IsSelectedProperty =
            SelectingItemsControl.IsSelectedProperty.AddOwner<MnemoTreeViewItem>();

        /// <summary>
        /// Defines the <see cref="Level"/> property.
        /// </summary>
        public static readonly DirectProperty<MnemoTreeViewItem, int> LevelProperty =
            AvaloniaProperty.RegisterDirect<MnemoTreeViewItem, int>(
                nameof(Level), o => o.Level);

        /// <summary>
        /// Defines the <see cref="Expanded"/> event.
        /// </summary>
        public static readonly RoutedEvent<RoutedEventArgs> ExpandedEvent =
            RoutedEvent.Register<MnemoTreeViewItem, RoutedEventArgs>(nameof(Expanded), RoutingStrategies.Bubble | RoutingStrategies.Tunnel);

        /// <summary>
        /// Defines the <see cref="Collapsed"/> event.
        /// </summary>
        public static readonly RoutedEvent<RoutedEventArgs> CollapsedEvent =
            RoutedEvent.Register<MnemoTreeViewItem, RoutedEventArgs>(nameof(Collapsed), RoutingStrategies.Bubble | RoutingStrategies.Tunnel);

        private static readonly FuncTemplate<Panel?> DefaultPanel =
            new(() => new StackPanel());

        private MnemoTreeView? _treeView;
        private Control? _header;
        private Control? _expandCollapseChevron;
        private int _level;
        private bool _templateApplied;
        private bool _deferredBringIntoViewFlag;

        static MnemoTreeViewItem()
        {
            SelectableMixin.Attach<MnemoTreeViewItem>(IsSelectedProperty);
            PressedMixin.Attach<MnemoTreeViewItem>();
            FocusableProperty.OverrideDefaultValue<MnemoTreeViewItem>(true);
            ItemsPanelProperty.OverrideDefaultValue<MnemoTreeViewItem>(DefaultPanel);
            RequestBringIntoViewEvent.AddClassHandler<MnemoTreeViewItem>((x, e) => x.OnRequestBringIntoView(e));
            IsExpandedProperty.Changed.AddClassHandler<MnemoTreeViewItem, bool>((x, e) => x.OnIsExpandedChanged(e));
        }

        private void OnIsExpandedChanged(AvaloniaPropertyChangedEventArgs<bool> args)
        {
            var routedEvent = args.NewValue.Value ? ExpandedEvent : CollapsedEvent;
            var eventArgs = new RoutedEventArgs() { RoutedEvent = routedEvent, Source = this };
            RaiseEvent(eventArgs);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the item is expanded to show its children.
        /// </summary>
        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        /// <summary>
        /// Gets or sets the selection state of the item.
        /// </summary>
        public bool IsSelected
        {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        /// <summary>
        /// Gets the level/indentation of the item.
        /// </summary>
        public int Level
        {
            get => _level;
            private set => SetAndRaise(LevelProperty, ref _level, value);
        }
        
        /// <summary>
        /// Occurs after the item has expanded to show its children.
        /// </summary>
        public event EventHandler<RoutedEventArgs>? Expanded
        {
            add => AddHandler(ExpandedEvent, value);
            remove => RemoveHandler(ExpandedEvent, value);
        }

        /// <summary>
        /// Occurs after the item has collapsed to hide its children.
        /// </summary>
        public event EventHandler<RoutedEventArgs>? Collapsed
        {
            add => AddHandler(CollapsedEvent, value);
            remove => RemoveHandler(CollapsedEvent, value);
        }

        internal MnemoTreeView? TreeViewOwner => _treeView;

        /// <inheritdoc />
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return base.OnCreateAutomationPeer()!;
        }

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        {
            return EnsureTreeView().CreateContainerForItemOverrideFromItem(item, index, recycleKey);
        }

        protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        {
            return EnsureTreeView().NeedsContainerOverrideFromItem(item, index, out recycleKey);
        }

        protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
        {
            EnsureTreeView().PrepareContainerForItemOverrideFromItem(container, item, index);
        }

        protected override void ContainerForItemPreparedOverride(Control container, object? item, int index)
        {
            EnsureTreeView().ContainerForItemPreparedOverrideFromItem(container, item, index);
        }

        /// <inheritdoc/>
        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnAttachedToLogicalTree(e);

            _treeView = this.GetLogicalAncestors().OfType<MnemoTreeView>().FirstOrDefault();

            Level = CalculateDistanceFromLogicalParent<MnemoTreeView>(this) - 1;

            if (ItemTemplate == null && _treeView?.ItemTemplate != null)
            {
                SetCurrentValue(ItemTemplateProperty, _treeView.ItemTemplate);
            }

            if (ItemContainerTheme == null && _treeView?.ItemContainerTheme != null)
            {
                SetCurrentValue(ItemContainerThemeProperty, _treeView.ItemContainerTheme);
            }

            ItemsView.CollectionChanged += OnItemsViewCollectionChanged;
        }

        /// <inheritdoc/>
        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            ItemsView.CollectionChanged -= OnItemsViewCollectionChanged;
            base.OnDetachedFromLogicalTree(e);
        }

        protected virtual void OnRequestBringIntoView(RequestBringIntoViewEventArgs e)
        {
            if (e.TargetObject == this)
            {
                if (!_templateApplied)
                {
                    _deferredBringIntoViewFlag = true;
                    return;
                }

                if (_header != null)
                {
                    var m = _header.TransformToVisual(this);

                    if (m.HasValue)
                    {
                        var bounds = new Rect(_header.Bounds.Size);
                        var rect = bounds.TransformToAABB(m.Value);
                        e.TargetRect = rect;
                    }
                }
            }
        }

        /// <inheritdoc/>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!e.Handled)
            {
                Func<MnemoTreeViewItem, bool>? handler =
                    e.Key switch
                    {
                        Key.Left => ApplyToItemOrRecursivelyIfCtrl(FocusAwareCollapseItem, e.KeyModifiers),
                        Key.Right => ApplyToItemOrRecursivelyIfCtrl(ExpandItem, e.KeyModifiers),
                        Key.Enter => ApplyToItemOrRecursivelyIfCtrl(IsExpanded ? CollapseItem : ExpandItem, e.KeyModifiers),
                        Key.Subtract => FocusAwareCollapseItem,
                        Key.Add => ExpandItem,
                        Key.Divide => ApplyToSubtree(CollapseItem),
                        Key.Multiply => ApplyToSubtree(ExpandItem),
                        _ => null,
                    };

                if (handler is not null)
                {
                    e.Handled = handler(this);
                }
                else
                {
                    TreeViewOwner?.UpdateSelectionFromEvent(this, e);
                }

                static Func<MnemoTreeViewItem, bool> ApplyToSubtree(Func<MnemoTreeViewItem, bool> f)
                {
                    return t => SubTree(t)
                        .ToList()
                        .Select(item => f(item))
                        .Aggregate(false, (p, c) => p || c);
                }

                static Func<MnemoTreeViewItem, bool> ApplyToItemOrRecursivelyIfCtrl(Func<MnemoTreeViewItem, bool> f, KeyModifiers keyModifiers)
                {
                    if ((keyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
                    {
                        return ApplyToSubtree(f);
                    }

                    return f;
                }

                static bool ExpandItem(MnemoTreeViewItem treeViewItem)
                {
                    if (treeViewItem.ItemCount > 0 && !treeViewItem.IsExpanded)
                    {
                        treeViewItem.SetCurrentValue(IsExpandedProperty, true);
                        return true;
                    }

                    return false;
                }

                static bool CollapseItem(MnemoTreeViewItem treeViewItem)
                {
                    if (treeViewItem.ItemCount > 0 && treeViewItem.IsExpanded)
                    {
                        treeViewItem.SetCurrentValue(IsExpandedProperty, false);
                        return true;
                    }

                    return false;
                }

                static bool FocusAwareCollapseItem(MnemoTreeViewItem treeViewItem)
                {
                    if (treeViewItem.ItemCount > 0 && treeViewItem.IsExpanded)
                    {
                        if (treeViewItem.IsFocused)
                        {
                            treeViewItem.SetCurrentValue(IsExpandedProperty, false);
                        }
                        else
                        {
                            treeViewItem.Focus(NavigationMethod.Directional);
                        }

                        return true;
                    }

                    return false;
                }

                static IEnumerable<MnemoTreeViewItem> SubTree(MnemoTreeViewItem treeViewItem)
                {
                    return new[] { treeViewItem }.Concat(treeViewItem.LogicalChildren.OfType<MnemoTreeViewItem>().SelectMany(child => SubTree(child)));
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            TreeViewOwner?.UpdateSelectionFromEvent(this, e);

            // Single-click expand/collapse (no double-tap delay). Only when click is on header area, not on the chevron.
            if (e.Handled || ItemCount == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (_expandCollapseChevron != null && e.Source is Visual sourceVisual &&
                sourceVisual.GetSelfAndVisualAncestors().Any(v => v == _expandCollapseChevron))
            {
                return;
            }

            if (_header != null && e.Source is Visual visual &&
                (visual == _header || visual.GetVisualAncestors().Any(a => a == _header)))
            {
                SetCurrentValue(IsExpandedProperty, !IsExpanded);
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            TreeViewOwner?.UpdateSelectionFromEvent(this, e);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            _header = e.NameScope.Find<Control>("PART_Header");
            _expandCollapseChevron = e.NameScope.Find<Control>("PART_ExpandCollapseChevron");
            _templateApplied = true;

            if (_deferredBringIntoViewFlag)
            {
                _deferredBringIntoViewFlag = false;
                Dispatcher.UIThread.Post(() => this.BringIntoView());
            }
        }

        private void OnItemsViewCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_treeView is null)
            {
                return;
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    foreach (var i in e.OldItems!)
                    {
                        _treeView.SelectedItems.Remove(i);
                    }

                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var i in GetRealizedContainers())
                    {
                        if (i is MnemoTreeViewItem tvi && tvi.IsSelected)
                        {
                            _treeView.SelectedItems.Remove(i.DataContext);
                        }
                    }

                    break;
            }
        }

        private static int CalculateDistanceFromLogicalParent<T>(ILogical? logical, int @default = -1)
            where T : class
        {
            var result = 0;

            while (logical != null && logical is not T)
            {
                ++result;
                logical = logical.LogicalParent;
            }

            return logical != null ? result : @default;
        }

        private MnemoTreeView EnsureTreeView() => _treeView ??
            throw new InvalidOperationException("The MnemoTreeViewItem is not part of a MnemoTreeView.");
    }
}
