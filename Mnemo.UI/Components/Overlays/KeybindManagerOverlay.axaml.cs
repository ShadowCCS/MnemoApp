using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Keybinds;
using Mnemo.UI.Services;
using Mnemo.UI;

namespace Mnemo.UI.Components.Overlays;

public partial class KeybindManagerOverlay : UserControl
{
    public Action? OnClose { get; set; }

    private KeybindManagerOverlayViewModel? _viewModel;

    public KeybindManagerOverlay()
        : this(
            ResolveRequiredService<IKeyMap>(),
            ResolveRequiredService<ILocalizationService>(),
            ResolveRequiredService<ISettingsService>())
    {
    }

    public KeybindManagerOverlay(IKeyMap keyMap, ILocalizationService localization, ISettingsService settings)
    {
        InitializeComponent();
        var vm = new KeybindManagerOverlayViewModel(keyMap, localization, settings);
        _viewModel = vm;
        DataContext = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        // ContentPresenter in overlay host can replace child DataContext; keep VM for bindings.
        AttachedToVisualTree += (_, _) =>
        {
            if (_viewModel != null)
                DataContext = _viewModel;
        };
        Loaded += async (_, _) => await vm.LoadAsync().ConfigureAwait(true);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null)
            return;
        if (e.PropertyName is not (nameof(KeybindManagerOverlayViewModel.IsShortcutEditorOpen)
            or nameof(KeybindManagerOverlayViewModel.EditorCaptureEnabled)))
            return;
        if (_viewModel.IsShortcutEditorOpen && _viewModel.EditorCaptureEnabled)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (KeyCaptureHost?.IsAttachedToVisualTree() == true)
                        KeyCaptureHost.Focus();
                },
                DispatcherPriority.Loaded);
        }
    }

    private void OnShortcutEditorCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel == null || !_viewModel.IsShortcutEditorOpen)
            return;

        if (e.Key == Key.Escape)
        {
            _viewModel.CancelShortcutEditorCommand?.Execute(null);
            e.Handled = true;
            return;
        }

        if (_viewModel.EditorDisableShortcut || IsModifierOnly(e.Key))
        {
            e.Handled = true;
            return;
        }

        var input = KeybindInputNormalizer.FromKeyEvent(e);
        var chord = new LogicalChord(input.Modifiers, input.KeyToken);
        _viewModel.ApplyCapturedChord(chord);
        e.Handled = true;
    }

    private static bool IsModifierOnly(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    private void OnEditShortcutTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_viewModel == null || sender is not Border border)
            return;
        if (border.Tag is not string actionId || string.IsNullOrEmpty(actionId))
            return;

        Dispatcher.UIThread.Post(
            () => _viewModel.RequestBeginEditShortcut(actionId),
            DispatcherPriority.Input);
    }

    private void OnEditorShortcutCancelClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel?.CancelShortcutEditorCommand?.Execute(null);
    }

    private void OnEditorShortcutRestoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel?.RestoreDefaultInEditorCommand?.Execute(null);
    }

    private void OnEditorShortcutSaveClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel?.SaveShortcutEditorCommand?.Execute(null);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.CancelShortcutEditorCommand?.Execute(null);
        OnClose?.Invoke();
    }

    private static T ResolveRequiredService<T>() where T : notnull
    {
        if (Avalonia.Application.Current is not App app || app.Services == null)
            throw new InvalidOperationException("Application services not available.");
        return app.Services.GetRequiredService<T>();
    }
}
