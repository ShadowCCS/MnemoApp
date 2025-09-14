using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MnemoApp.UI.Components;
using MnemoApp.Core.MnemoAPI;

namespace MnemoApp.Modules.Paths.Overlays
{
    public partial class CreatePathOverlay : UserControl
    {
        private readonly IMnemoAPI _mnemoAPI;
        
        public CreatePathOverlay(IMnemoAPI mnemoAPI)
        {
            _mnemoAPI = mnemoAPI ?? throw new System.ArgumentNullException(nameof(mnemoAPI));
            InitializeComponent();
            
            var inputBuilder = this.FindControl<InputBuilder>("InputBuilderControl");
            if (inputBuilder != null)
            {
                var vm = new InputBuilderViewModel(_mnemoAPI);
                vm.ApplyConfigurationFromControl(inputBuilder.InputMethods);
                vm.HeaderNamespace = inputBuilder.HeaderNamespace;
                vm.TitleKey = inputBuilder.TitleKey;
                vm.DescriptionKey = inputBuilder.DescriptionKey;
                inputBuilder.DataContext = vm;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}


