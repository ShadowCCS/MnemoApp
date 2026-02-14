using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Overview.ViewModels;

/// <summary>
/// Base class for widget content ViewModels.
/// Provides common functionality for all widgets.
/// </summary>
public abstract class WidgetViewModelBase : ViewModelBase, IWidgetViewModel
{
    /// <summary>
    /// Initializes the widget when added to the dashboard.
    /// Override to load data or subscribe to events.
    /// </summary>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleans up resources when the widget is removed.
    /// Override to unsubscribe from events or dispose resources.
    /// </summary>
    public virtual void Dispose()
    {
        // Base implementation does nothing
    }
}
