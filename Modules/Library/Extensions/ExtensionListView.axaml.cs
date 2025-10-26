using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;

namespace MnemoApp.Modules.Library.Extensions
{
    public partial class ExtensionListView : UserControl
    {
        public ExtensionListView()
        {
            InitializeComponent();

            // Set DataContext to ExtensionListViewModel if not already set
            if (DataContext == null)
            {
                var serviceProvider = ApplicationHost.GetServiceProvider();
                DataContext = serviceProvider.GetRequiredService<ExtensionListViewModel>();
            }
        }
    }
}

