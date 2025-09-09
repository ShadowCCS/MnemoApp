using Avalonia.Controls;
using Avalonia;

namespace MnemoApp.UI.Components
{
    public partial class InputBuilder : UserControl
    {
        public static readonly StyledProperty<int> UsableContextProperty =
            AvaloniaProperty.Register<InputBuilder, int>(nameof(UsableContext), 0);

        public int UsableContext
        {
            get => GetValue(UsableContextProperty);
            set => SetValue(UsableContextProperty, value);
        }

        public static readonly StyledProperty<string> InputMethodsProperty =
            AvaloniaProperty.Register<InputBuilder, string>(nameof(InputMethods), "Text,Files,Links");

        public string InputMethods
        {
            get => GetValue(InputMethodsProperty);
            set => SetValue(InputMethodsProperty, value);
        }

        public InputBuilder()
        {
            InitializeComponent();
        }
    }
}
