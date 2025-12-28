using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Mnemo.Core.Services;
using Mnemo.UI.Components.Overlays;

namespace Mnemo.UI.Services;

public class OverlayService : IOverlayService
{
    public ObservableCollection<OverlayInstance> Overlays { get; } = new();
    private readonly Dictionary<string, TaskCompletionSource<object?>> _completionSources = new();
    private readonly Dictionary<string, Type> _typeCache = new();

    public void Show(string overlayName, object? parameter = null)
    {
        if (!_typeCache.TryGetValue(overlayName, out var type))
        {
            // Simple registry-less resolution: search for the type by name
            type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == overlayName || t.FullName == overlayName);
            
            if (type != null) _typeCache[overlayName] = type;
        }

        if (type == null) return;

        var content = Activator.CreateInstance(type);
        CreateOverlay(content!, new OverlayOptions(), overlayName);
    }

    public void Hide()
    {
        if (Overlays.Any())
        {
            var last = Overlays.Last();
            CloseOverlay(last.Id);
        }
    }

    public void CloseOverlay(string id) => CloseOverlay(id, null);

    public void CloseOverlay(string id, object? result)
    {
        var overlay = Overlays.FirstOrDefault(o => o.Id == id);
        if (overlay != null)
        {
            Overlays.Remove(overlay);
            if (_completionSources.TryGetValue(id, out var tcs))
            {
                tcs.TrySetResult(result);
                _completionSources.Remove(id);
            }
        }
    }

    public string CreateOverlay(object content, OverlayOptions options, string? name = null)
    {
        var instance = new OverlayInstance
        {
            Content = content,
            Options = options,
            Name = name ?? string.Empty,
            ZIndex = Overlays.Count + 1000 // Ensure they stack correctly
        };
        Overlays.Add(instance);
        return instance.Id;
    }

    public async Task<string?> CreateDialogAsync(string title, string message, string confirmText = "OK", string cancelText = "", object? icon = null, object? parameter = null)
    {
        var tcs = new TaskCompletionSource<object?>();
        var dialog = new DialogOverlay
        {
            Title = title,
            Description = message,
            PrimaryText = confirmText,
            SecondaryText = cancelText
        };

        var options = new OverlayOptions
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = false
        };

        var id = CreateOverlay(dialog, options, "Dialog");
        _completionSources[id] = tcs;

        // Hook into the dialog's choose action
        dialog.OnChoose = (result) => CloseOverlay(id, result);

        var result = await tcs.Task;
        return result as string;
    }
}

