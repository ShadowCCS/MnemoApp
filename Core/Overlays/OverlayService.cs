using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MnemoApp.Core.Common;

namespace MnemoApp.Core.Overlays;

public class OverlayViewModelWrapper
{
    public ViewModelBase? ViewModel { get; set; }
    public OverlayOptions Options { get; set; } = new();
    public string Id { get; set; } = Guid.NewGuid().ToString();

    private readonly TaskCompletionSource<object?> _tcs = new();
    public Task<object?> WaitForResultAsync() => _tcs.Task;

    public void Complete(object? result)
    {
        _tcs.TrySetResult(result);
    }
}

public class OverlayService : IOverlayService
{
    private readonly ObservableCollection<OverlayViewModelWrapper> _overlays = new();
    public ObservableCollection<OverlayViewModelWrapper> Overlays => _overlays;

    public async Task<object?> ShowOverlay<TViewModel>(
        object? dataContext = null,
        OverlayOptions? options = null
    ) where TViewModel : ViewModelBase
    {
        return await PushOverlay<TViewModel>(dataContext, options);
    }

    public async Task<object?> PushOverlay<TViewModel>(
        object? dataContext = null,
        OverlayOptions? options = null
    ) where TViewModel : ViewModelBase
    {
        var vm = Activator.CreateInstance<TViewModel>();
        
        // Initialize with data context if provided and supported
        if (dataContext != null && vm is ViewModelBase viewModelBase)
        {
            // You can add custom initialization logic here if needed
            // For now, we'll just store the dataContext in a property if it exists
        }

        var wrapper = new OverlayViewModelWrapper
        {
            ViewModel = vm,
            Options = options ?? new OverlayOptions()
        };

        _overlays.Add(wrapper);

        return await wrapper.WaitForResultAsync();
    }

    public OverlayViewModelWrapper? ReplaceOverlay<TViewModel>(
        object? targetOverlay,
        object? dataContext = null,
        OverlayOptions? options = null
    ) where TViewModel : ViewModelBase
    {
        var targetWrapper = _overlays.FirstOrDefault(o => o.ViewModel == targetOverlay);
        if (targetWrapper == null)
            return null;

        var targetIndex = _overlays.IndexOf(targetWrapper);
        
        var vm = Activator.CreateInstance<TViewModel>();
        
        // Initialize with data context if provided and supported
        if (dataContext != null && vm is ViewModelBase viewModelBase)
        {
            // You can add custom initialization logic here if needed
        }

        var newWrapper = new OverlayViewModelWrapper
        {
            ViewModel = vm,
            Options = options ?? new OverlayOptions()
        };

        // Complete the old overlay and replace it
        targetWrapper.Complete(null);
        _overlays[targetIndex] = newWrapper;

        return newWrapper;
    }

    public bool PopOverlay()
    {
        if (_overlays.Count == 0)
            return false;

        var topOverlay = _overlays.Last();
        topOverlay.Complete(null);
        _overlays.Remove(topOverlay);
        return true;
    }

    public bool PopOverlay(object? overlay)
    {
        var target = _overlays.FirstOrDefault(o => o.ViewModel == overlay);
        if (target == null)
            return false;

        target.Complete(null);
        _overlays.Remove(target);
        return true;
    }

    public OverlayViewModelWrapper? PeekOverlay()
    {
        return _overlays.LastOrDefault();
    }

    public int GetOverlayCount()
    {
        return _overlays.Count;
    }

    public void CloseOverlay(object? overlay)
    {
        PopOverlay(overlay);
    }

    public void CloseOverlayWithResult(object? overlay, object? result)
    {
        var target = _overlays.FirstOrDefault(o => o.ViewModel == overlay);
        if (target != null)
        {
            target.Complete(result); // Complete the task with result
            _overlays.Remove(target);
        }
    }

    public OverlayViewModelWrapper? GetWrapper(object? viewModel)
    {
        return _overlays.FirstOrDefault(o => o.ViewModel == viewModel);
    }

    public void CloseAllOverlays()
    {
        foreach (var overlay in _overlays.ToList())
        {
            overlay.Complete(null); // Complete all pending tasks
        }
        _overlays.Clear();
    }
}