using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Mnemo.Core;
using Mnemo.Core.Services;

namespace Mnemo.UI.Components
{
    public class SettingsCard : TemplatedControl
    {
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<SettingsCard, string?>(nameof(Title));

        public static readonly StyledProperty<string?> DescriptionProperty =
            AvaloniaProperty.Register<SettingsCard, string?>(nameof(Description));

        public static readonly StyledProperty<object?> RightContentProperty =
            AvaloniaProperty.Register<SettingsCard, object?>(nameof(RightContent));

        // Localization properties
        public static readonly StyledProperty<string?> TitleKeyProperty =
            AvaloniaProperty.Register<SettingsCard, string?>(nameof(TitleKey));

        public static readonly StyledProperty<string?> DescriptionKeyProperty =
            AvaloniaProperty.Register<SettingsCard, string?>(nameof(DescriptionKey));

        public static readonly StyledProperty<string?> NamespaceProperty =
            AvaloniaProperty.Register<SettingsCard, string?>(nameof(Namespace));

        static SettingsCard()
        {
            TitleKeyProperty.Changed.AddClassHandler<SettingsCard>((card, _) => card.UpdateLocalizedTexts());
            DescriptionKeyProperty.Changed.AddClassHandler<SettingsCard>((card, _) => card.UpdateLocalizedTexts());
            NamespaceProperty.Changed.AddClassHandler<SettingsCard>((card, _) => card.UpdateLocalizedTexts());
        }

        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string? Description
        {
            get => GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public object? RightContent
        {
            get => GetValue(RightContentProperty);
            set => SetValue(RightContentProperty, value);
        }

        public string? TitleKey
        {
            get => GetValue(TitleKeyProperty);
            set => SetValue(TitleKeyProperty, value);
        }

        public string? DescriptionKey
        {
            get => GetValue(DescriptionKeyProperty);
            set => SetValue(DescriptionKeyProperty, value);
        }

        public string? Namespace
        {
            get => GetValue(NamespaceProperty);
            set => SetValue(NamespaceProperty, value);
        }

        private ILocalizationService? _locService;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateLocalizedTexts();
            
            // Subscribe to language changes
            _locService = ((App)Application.Current!).Services!.GetService(typeof(ILocalizationService)) as ILocalizationService;
            if (_locService != null)
            {
                _locService.LanguageChanged += OnLanguageChanged;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            
            // Unsubscribe from language changes
            if (_locService != null)
            {
                _locService.LanguageChanged -= OnLanguageChanged;
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            UpdateLocalizedTexts();
        }

        private void UpdateLocalizedTexts()
        {
            var locService = _locService ?? ((App)Application.Current!).Services!.GetService(typeof(ILocalizationService)) as ILocalizationService;
            if (locService == null || string.IsNullOrWhiteSpace(Namespace)) return;

            if (!string.IsNullOrWhiteSpace(TitleKey))
            {
                Title = locService.T(Namespace!, TitleKey!);
            }

            if (!string.IsNullOrWhiteSpace(DescriptionKey))
            {
                Description = locService.T(Namespace!, DescriptionKey!);
            }
        }
    }
}

