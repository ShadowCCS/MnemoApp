namespace Mnemo.Core.Services;

/// <summary>
/// Interface for user-configurable widget settings.
/// Widget-specific implementations provide persistence and configuration UI support.
/// </summary>
public interface IWidgetSettings
{
    /// <summary>
    /// Gets the unique identifier of the widget type these settings belong to.
    /// </summary>
    string WidgetId { get; }

    /// <summary>
    /// Serializes the settings to a string for persistence.
    /// </summary>
    string Serialize();

    /// <summary>
    /// Deserializes the settings from a persisted string.
    /// </summary>
    void Deserialize(string data);
}
