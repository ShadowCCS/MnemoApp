using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;

namespace Mnemo.UI.Components.Overlays;

public sealed class LinkEditDialogResult
{
    public string? Url { get; init; }
    public string DisplayText { get; init; } = string.Empty;
    public bool RemoveLinkRequested { get; init; }
}

public partial class LinkInsertDialogOverlay : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(Title));

    public static readonly StyledProperty<string?> UrlLabelProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(UrlLabel));

    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(Url));

    public static readonly StyledProperty<string?> UrlPlaceholderProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(UrlPlaceholder));

    public static readonly StyledProperty<string?> DisplayLabelProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(DisplayLabel));

    public static readonly StyledProperty<string?> DisplayTextProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(DisplayText));

    public static readonly StyledProperty<string?> DisplayPlaceholderProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(DisplayPlaceholder));

    public static readonly StyledProperty<bool> ShowDisplaySectionProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, bool>(nameof(ShowDisplaySection), defaultValue: true);

    public static readonly StyledProperty<bool> ShowCrossBlockHintProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, bool>(nameof(ShowCrossBlockHint));

    public static readonly StyledProperty<string?> CrossBlockHintProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(CrossBlockHint));

    public static readonly StyledProperty<bool> ShowRemoveLinkProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, bool>(nameof(ShowRemoveLink));

    public static readonly StyledProperty<string?> RemoveLinkTextProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(RemoveLinkText));

    public static readonly StyledProperty<string?> ConfirmTextProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(ConfirmText), defaultValue: "OK");

    public static readonly StyledProperty<string?> CancelTextProperty =
        AvaloniaProperty.Register<LinkInsertDialogOverlay, string?>(nameof(CancelText), defaultValue: "Cancel");

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? UrlLabel { get => GetValue(UrlLabelProperty); set => SetValue(UrlLabelProperty, value); }
    public string? Url { get => GetValue(UrlProperty); set => SetValue(UrlProperty, value); }
    public string? UrlPlaceholder { get => GetValue(UrlPlaceholderProperty); set => SetValue(UrlPlaceholderProperty, value); }
    public string? DisplayLabel { get => GetValue(DisplayLabelProperty); set => SetValue(DisplayLabelProperty, value); }
    public string? DisplayText { get => GetValue(DisplayTextProperty); set => SetValue(DisplayTextProperty, value); }
    public string? DisplayPlaceholder { get => GetValue(DisplayPlaceholderProperty); set => SetValue(DisplayPlaceholderProperty, value); }
    public bool ShowDisplaySection { get => GetValue(ShowDisplaySectionProperty); set => SetValue(ShowDisplaySectionProperty, value); }
    public bool ShowCrossBlockHint { get => GetValue(ShowCrossBlockHintProperty); set => SetValue(ShowCrossBlockHintProperty, value); }
    public string? CrossBlockHint { get => GetValue(CrossBlockHintProperty); set => SetValue(CrossBlockHintProperty, value); }
    public bool ShowRemoveLink { get => GetValue(ShowRemoveLinkProperty); set => SetValue(ShowRemoveLinkProperty, value); }
    public string? RemoveLinkText { get => GetValue(RemoveLinkTextProperty); set => SetValue(RemoveLinkTextProperty, value); }
    public string? ConfirmText { get => GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
    public string? CancelText { get => GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RemoveLinkCommand { get; }
    public ICommand CloseCommand { get; }

    public Action<LinkEditDialogResult?>? OnResult { get; set; }

    /// <summary>When true, Confirm requires a non-empty URL (new link).</summary>
    public bool RequireUrlForConfirm { get; set; }

    public LinkInsertDialogOverlay()
    {
        ConfirmCommand = new RelayCommand(OnConfirm);
        CancelCommand = new RelayCommand(OnCancel);
        RemoveLinkCommand = new RelayCommand(OnRemoveLink);
        CloseCommand = new RelayCommand(OnCancel);
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        Loaded += OnDialogLoaded;
    }

    private void OnDialogLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<TextBox>("UrlTextBox") is { } urlBox)
        {
            urlBox.Focus();
            if (string.IsNullOrEmpty(Url))
                urlBox.SelectAll();
        }
    }

    private void OnConfirm()
    {
        if (RequireUrlForConfirm && string.IsNullOrWhiteSpace(Url))
            return;
        if (ShowDisplaySection && string.IsNullOrWhiteSpace(DisplayText))
            return;
        if (!RequireUrlForConfirm && string.IsNullOrWhiteSpace(Url))
        {
            OnRemoveLink();
            return;
        }

        OnResult?.Invoke(new LinkEditDialogResult
        {
            Url = Url?.Trim(),
            DisplayText = DisplayText?.Trim() ?? string.Empty,
            RemoveLinkRequested = false
        });
    }

    private void OnRemoveLink()
    {
        OnResult?.Invoke(new LinkEditDialogResult { RemoveLinkRequested = true });
    }

    private void OnCancel() => OnResult?.Invoke(null);
}
