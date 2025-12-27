using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;

namespace Mnemo.UI.Components.Overlays
{
    public partial class InputDialogOverlay : UserControl
    {
        public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<InputDialogOverlay, string?>(nameof(Title));
        public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<InputDialogOverlay, string?>(nameof(Placeholder));
        public static readonly StyledProperty<string?> InputValueProperty = AvaloniaProperty.Register<InputDialogOverlay, string?>(nameof(InputValue));
        public static readonly StyledProperty<string?> ConfirmTextProperty = AvaloniaProperty.Register<InputDialogOverlay, string?>(nameof(ConfirmText), defaultValue: "OK");
        public static readonly StyledProperty<string?> CancelTextProperty = AvaloniaProperty.Register<InputDialogOverlay, string?>(nameof(CancelText), defaultValue: "Cancel");

        public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
        public string? InputValue { get => GetValue(InputValueProperty); set => SetValue(InputValueProperty, value); }
        public string? ConfirmText { get => GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
        public string? CancelText { get => GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public Action<string?>? OnResult { get; set; }

        public InputDialogOverlay()
        {
            ConfirmCommand = new RelayCommand(OnConfirm);
            CancelCommand = new RelayCommand(OnCancel);
            InitializeComponent();
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            
            // Focus the text box when loaded
            var textBox = this.FindControl<TextBox>("InputTextBox");
            if (textBox != null)
            {
                textBox.AttachedToVisualTree += (s, e) =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                };
            }
        }

        private void OnConfirm()
        {
            OnResult?.Invoke(InputValue);
        }

        private void OnCancel()
        {
            OnResult?.Invoke(null);
        }
    }
}



