using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MnemoApp.Core.Common;
using MnemoApp.Core.Overlays;

namespace MnemoApp.Core.Overlays;

public interface IOverlayService
{
    // Collection of active overlays (stack)
    ObservableCollection<OverlayViewModelWrapper> Overlays { get; }

    // Original overlay methods
    Task<object?> ShowOverlay<TViewModel>(
        object? dataContext = null,
        OverlayOptions? options = null
    ) where TViewModel : ViewModelBase;

    void CloseOverlay(object? overlay); // Close by reference
    void CloseOverlayWithResult(object? overlay, object? result); // Close with result
    OverlayViewModelWrapper? GetWrapper(object? viewModel); // Get wrapper for direct communication
    void CloseAllOverlays();

    // Stack-based overlay methods
    Task<object?> PushOverlay<TViewModel>(
        object? dataContext = null,
        OverlayOptions? options = null
    ) where TViewModel : ViewModelBase;

    OverlayViewModelWrapper? ReplaceOverlay<TViewModel>(
        object? targetOverlay,
        object? dataContext = null,
        OverlayOptions? options = null
    ) where TViewModel : ViewModelBase;

    bool PopOverlay(); // Pop the top overlay
    bool PopOverlay(object? overlay); // Pop specific overlay
    OverlayViewModelWrapper? PeekOverlay(); // Get top overlay without removing
    int GetOverlayCount(); // Get number of active overlays
}