using Avalonia.Controls;
using Avalonia;
using System;

namespace Mnemo.UI.Components
{
    public partial class InputBuilder : UserControl
    {
        public static readonly StyledProperty<string> InputMethodsProperty =
            AvaloniaProperty.Register<InputBuilder, string>(nameof(InputMethods), "Text,Files,Links");

        public static readonly StyledProperty<string> HeaderNamespaceProperty =
            AvaloniaProperty.Register<InputBuilder, string>(nameof(HeaderNamespace), "InputBuilder");

        public static readonly StyledProperty<string> TitleKeyProperty =
            AvaloniaProperty.Register<InputBuilder, string>(nameof(TitleKey), "Title");

        public static readonly StyledProperty<string> DescriptionKeyProperty =
            AvaloniaProperty.Register<InputBuilder, string>(nameof(DescriptionKey), "Description");

        public string InputMethods
        {
            get => GetValue(InputMethodsProperty);
            set => SetValue(InputMethodsProperty, value);
        }

        public string HeaderNamespace
        {
            get => GetValue(HeaderNamespaceProperty);
            set => SetValue(HeaderNamespaceProperty, value);
        }

        public string TitleKey
        {
            get => GetValue(TitleKeyProperty);
            set => SetValue(TitleKeyProperty, value);
        }

        public string DescriptionKey
        {
            get => GetValue(DescriptionKeyProperty);
            set => SetValue(DescriptionKeyProperty, value);
        }

        public InputBuilder()
        {
            InitializeComponent();

            this.DataContextChanged += (_, __) => ApplyPropsToViewModel();

            // Ensure initial styling/bindings evaluate after load
            this.AttachedToVisualTree += (_, __) =>
            {
                if (DataContext is InputBuilderViewModel vm)
                {
                    // Nudge active tab state to force initial converter evaluation
                    vm.ConfigureTabs(vm.ShowTextTab, vm.ShowFilesTab, vm.ShowLinksTab);
                }
            };

            this.DetachedFromVisualTree += (_, __) =>
            {
                if (DataContext is InputBuilderViewModel vm)
                {
                    vm.Dispose();
                }
            };
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == InputMethodsProperty ||
                change.Property == HeaderNamespaceProperty ||
                change.Property == TitleKeyProperty ||
                change.Property == DescriptionKeyProperty)
            {
                ApplyPropsToViewModel();
            }
        }

        private void ApplyPropsToViewModel()
        {
            if (DataContext is InputBuilderViewModel vm)
            {
                vm.ApplyConfigurationFromControl(InputMethods);
                vm.HeaderNamespace = HeaderNamespace ?? string.Empty;
                vm.TitleKey = TitleKey ?? string.Empty;
                vm.DescriptionKey = DescriptionKey ?? string.Empty;
            }
        }
    }
}

