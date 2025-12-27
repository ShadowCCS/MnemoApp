using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Mnemo.UI.Components.Overlays
{
    public partial class DialogOverlay : UserControl
    {
        private readonly ILocalizationService _loc;

        public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<DialogOverlay, string?>(nameof(Title));
        public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<DialogOverlay, string?>(nameof(Description));
        public static readonly StyledProperty<string?> PrimaryTextProperty = AvaloniaProperty.Register<DialogOverlay, string?>(nameof(PrimaryText));
        public static readonly StyledProperty<string?> SecondaryTextProperty = AvaloniaProperty.Register<DialogOverlay, string?>(nameof(SecondaryText));

        public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
        public string? PrimaryText { get => GetValue(PrimaryTextProperty); set => SetValue(PrimaryTextProperty, value); }
        public string? SecondaryText { get => GetValue(SecondaryTextProperty); set => SetValue(SecondaryTextProperty, value); }

        public ICommand PrimaryCommand { get; }
        public ICommand SecondaryCommand { get; }

        public Action<string?>? OnChoose { get; set; }

        public DialogOverlay()
        {
            _loc = ((App)Application.Current!).Services!.GetRequiredService<ILocalizationService>();
            PrimaryCommand = new RelayCommand(OnPrimary);
            SecondaryCommand = new RelayCommand(OnSecondary);
            InitializeComponent();
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnPrimary()
        {
            OnChoose?.Invoke(PrimaryText ?? _loc.T("Primary", "Common"));
        }

        private void OnSecondary()
        {
            OnChoose?.Invoke(SecondaryText ?? _loc.T("Secondary", "Common"));
        }
    }
}



