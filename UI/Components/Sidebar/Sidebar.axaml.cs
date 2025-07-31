using Avalonia;
using Avalonia.Controls;

namespace MnemoApp.UI.Components.Sidebar
{
    public partial class Sidebar : UserControl
    {
        public static readonly StyledProperty<bool> IsCollapsedProperty =
            AvaloniaProperty.Register<Sidebar, bool>(nameof(IsCollapsed), false);

        public bool IsCollapsed
        {
            get => GetValue(IsCollapsedProperty);
            set => SetValue(IsCollapsedProperty, value);
        }

        public Sidebar()
        {
            InitializeComponent();
        }
    }
}