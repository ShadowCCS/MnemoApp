using System.Linq;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.UI.Components.Overlays;
using Mnemo.UI;

namespace Mnemo.UI.Services;

/// <summary>Opens the keybind manager overlay; replaces an existing instance with the same name.</summary>
public static class KeybindManagerUi
{
    public const string OverlayName = "KeybindManager";

    /// <summary>Closes the catalog if it is open; otherwise opens it. Used when <see cref="KeybindActionDefinition.ToggleOnRepeat"/> is set.</summary>
    public static void TryToggle(IOverlayService overlays, IKeyMap keyMap)
    {
        var existing = overlays.Overlays.Where(o => o.Name == OverlayName).ToList();
        if (existing.Count > 0)
        {
            foreach (var o in existing)
                overlays.CloseOverlay(o.Id);
            return;
        }

        TryOpen(overlays, keyMap);
    }

    public static void TryOpen(IOverlayService overlays, IKeyMap keyMap)
    {
        foreach (var existing in overlays.Overlays.ToList())
        {
            if (existing.Name == OverlayName)
                overlays.CloseOverlay(existing.Id);
        }

        if (Application.Current is not App app || app.Services == null)
            return;
        var localization = app.Services.GetRequiredService<ILocalizationService>();
        var settings = app.Services.GetRequiredService<ISettingsService>();
        var view = new KeybindManagerOverlay(keyMap, localization, settings);
        var id = overlays.CreateOverlay(
            view,
            new OverlayOptions
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                ShowBackdrop = true,
                CloseOnOutsideClick = true,
            },
            OverlayName);

        view.OnClose = () => overlays.CloseOverlay(id);
    }
}
