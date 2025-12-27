using Avalonia;
using Avalonia.Controls;

namespace Mnemo.UI.Controls
{
    public class NavigationButton : Button
    {
        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<NavigationButton, bool>(nameof(IsSelected));

        public static readonly StyledProperty<bool> IsCollapsedProperty =
            AvaloniaProperty.Register<NavigationButton, bool>(nameof(IsCollapsed));

        static NavigationButton()
        {
            IsSelectedProperty.Changed.AddClassHandler<NavigationButton>((x, e) => x.UpdateClasses());
            IsCollapsedProperty.Changed.AddClassHandler<NavigationButton>((x, e) => x.UpdateClasses());
        }

        public bool IsSelected
        {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public bool IsCollapsed
        {
            get => GetValue(IsCollapsedProperty);
            set => SetValue(IsCollapsedProperty, value);
        }

        private void UpdateClasses()
        {
            PseudoClasses.Set(":selected", IsSelected);
            PseudoClasses.Set(":collapsed", IsCollapsed);
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateClasses();
        }
    }
}

