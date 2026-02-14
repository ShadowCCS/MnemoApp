namespace Mnemo.Core.Services;

/// <summary>
/// Interface for widget content ViewModels (runtime instance).
/// Each widget's logic implements this interface.
/// </summary>
public interface IWidgetViewModel
{
    /// <summary>
    /// Called when the widget is initialized and added to the dashboard.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Called when the widget is being removed from the dashboard.
    /// Use this to clean up resources, unsubscribe from events, etc.
    /// </summary>
    void Dispose();
}
