using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MnemoApp.UI.Components;

namespace MnemoApp.UI.Components.Overlays
{
    public partial class CreatePathOverlay : UserControl
    {
        public CreatePathOverlay()
        {
            InitializeComponent();
            
            // Find the InputBuilder and set its DataContext
            var inputBuilder = this.FindControl<InputBuilder>("InputBuilderControl");
            if (inputBuilder != null)
            {
                var vm = new InputBuilderViewModel();
                // Pull optional properties off the control to configure the VM
                vm.ApplyConfigurationFromControl(inputBuilder.UsableContext, inputBuilder.InputMethods);
                inputBuilder.DataContext = vm;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
