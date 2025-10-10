using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MnemoApp.UI.Components
{
    public partial class Breadcrumb : UserControl
    {
        public Breadcrumb()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

