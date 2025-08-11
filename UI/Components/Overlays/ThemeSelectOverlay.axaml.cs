using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;
using MnemoApp.Core.Overlays;

namespace MnemoApp.UI.Components.Overlays
{
    public partial class ThemeSelectOverlay : UserControl
    {
        public string OverlayName { get; set; } = "ThemeSelectOverlay";
        public string SelectedTheme { get; set; } = "Dawn";
        public ICommand ConfirmCommand { get; }

        public ThemeSelectOverlay()
        {
            InitializeComponent();
            ConfirmCommand = new RelayCommand(ConfirmSelection);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        public void ConfirmSelection()
        {
            var overlays = ApplicationHost.Services.GetRequiredService<IOverlayService>();
            overlays.CloseOverlay(OverlayName, SelectedTheme);
        }
    }
}


