using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Mnemo.UI.Components.Overlays;

public partial class GlobalSearchOverlay : UserControl
{
    public Action? OnClose { get; set; }
    private readonly GlobalSearchOverlayViewModel _viewModel;

    public GlobalSearchOverlay()
        : this(
            ResolveRequiredService<IGlobalSearchService>(),
            ResolveRequiredService<INavigationService>(),
            ResolveRequiredService<ILocalizationService>(),
            string.Empty)
    {
    }

    public GlobalSearchOverlay(
        IGlobalSearchService globalSearchService,
        INavigationService navigationService,
        ILocalizationService localizationService,
        string placeholderText)
    {
        _viewModel = new GlobalSearchOverlayViewModel(globalSearchService, navigationService, localizationService, placeholderText);
        _viewModel.CloseAction = () => OnClose?.Invoke();
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<TextBox>("SearchBox") is { } box)
        {
            box.Focus();
        }

        await _viewModel.SearchAsync(_viewModel.SearchText).ConfigureAwait(false);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            e.Handled = true;
            await _viewModel.MoveSelectionAsync(1).ConfigureAwait(false);
            return;
        }

        if (e.Key == Key.Up)
        {
            e.Handled = true;
            await _viewModel.MoveSelectionAsync(-1).ConfigureAwait(false);
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _viewModel.ActivateSelectionAsync().ConfigureAwait(false);
        }
    }

    private static T ResolveRequiredService<T>() where T : notnull
    {
        if (Avalonia.Application.Current is not App app || app.Services == null)
        {
            throw new InvalidOperationException("Application services are not available for GlobalSearchOverlay.");
        }

        return app.Services.GetRequiredService<T>();
    }
}
