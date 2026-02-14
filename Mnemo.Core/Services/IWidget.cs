using Mnemo.Core.Models.Widgets;

namespace Mnemo.Core.Services;

/// <summary>
/// Factory interface for creating dashboard widgets.
/// Provides metadata and creates runtime instances of widget ViewModels.
/// </summary>
public interface IWidget
{
    /// <summary>
    /// Gets the metadata for this widget type.
    /// </summary>
    WidgetMetadata Metadata { get; }

    /// <summary>
    /// Creates a new instance of the widget's ViewModel.
    /// </summary>
    /// <param name="settings">Optional settings for this widget instance.</param>
    /// <returns>A new widget ViewModel instance.</returns>
    IWidgetViewModel CreateViewModel(IWidgetSettings? settings = null);
}
