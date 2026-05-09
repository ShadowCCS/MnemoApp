using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.UI.Services;

namespace Mnemo.UI.Components;

public partial class WorkspaceKeybindHost : ContentControl
{
    public WorkspaceKeybindHost()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (sender is not WorkspaceKeybindHost) return;
        if (Application.Current is not App app || app.Services == null) return;

        var keyMap = app.Services.GetService(typeof(IKeyMap)) as IKeyMap;
        var router = app.Services.GetService(typeof(IKeybindActionRouter)) as IKeybindActionRouter;
        if (keyMap == null || router == null) return;

        // Mindmap canvas matches locals in bubble phase (MindmapView) so TextBox editing keeps Tab/Enter/etc.
        if (app.Services.GetService(typeof(INavigationService)) is INavigationService nav
            && string.Equals(nav.CurrentRoute, "mindmap-detail", StringComparison.Ordinal))
            return;

        var input = KeybindInputNormalizer.FromKeyEvent(e);
        var r = keyMap.ProcessLocalKeyDown(input, DateTime.UtcNow, SequenceSwallowMode.SwallowOnPrefixAdvance);
        if (r.CompletedAction && !string.IsNullOrEmpty(r.ActionId) && !router.TryExecute(r.ActionId))
            return;
        if (r.Handled)
            e.Handled = true;
    }
}
