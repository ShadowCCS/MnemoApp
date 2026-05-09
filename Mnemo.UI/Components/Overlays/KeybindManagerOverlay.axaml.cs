using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;
using Mnemo.UI;

namespace Mnemo.UI.Components.Overlays;

public partial class KeybindManagerOverlay : UserControl
{
    public Action? OnClose { get; set; }

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
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync().ConfigureAwait(true);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => OnClose?.Invoke();

    private static T ResolveRequiredService<T>() where T : notnull
    {
        if (Avalonia.Application.Current is not App app || app.Services == null)
            throw new InvalidOperationException("Application services not available.");
        return app.Services.GetRequiredService<T>();
    }
}
