namespace Mnemo.UI.Modules.Overview.Widgets;

/// <summary>
/// Builds avares URIs for each widget's co-located <c>icon.svg</c>
/// (<c>Modules/Overview/Widgets/&lt;WidgetFolder&gt;/icon.svg</c>).
/// </summary>
public static class WidgetIconAvares
{
    private const string Root = "avares://Mnemo.UI/Modules/Overview/Widgets";

    /// <param name="widgetFolder">
    /// Folder name under <c>Modules/Overview/Widgets</c> that contains <c>icon.svg</c>.
    /// </param>
    public static string Uri(string widgetFolder)
    {
        if (string.IsNullOrWhiteSpace(widgetFolder))
            throw new ArgumentException("Widget folder name is required.", nameof(widgetFolder));
        return $"{Root}/{widgetFolder.Trim().Trim('/')}/icon.svg";
    }
}
