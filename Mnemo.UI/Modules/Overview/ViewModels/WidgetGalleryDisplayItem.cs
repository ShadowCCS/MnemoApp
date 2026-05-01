using System.Collections.Generic;
using Mnemo.Core.Models.Widgets;

namespace Mnemo.UI.Modules.Overview.ViewModels;

/// <summary>
/// Row in the Add Widget gallery with precomposed strings for binding and search.
/// </summary>
public sealed class WidgetGalleryDisplayItem
{
    public WidgetGalleryDisplayItem(
        WidgetMetadata metadata,
        string resolvedTitle,
        string metaLine,
        string resolvedGalleryDescription,
        IReadOnlyList<string> tagLabels,
        string searchBlob,
        bool isOnDashboard,
        string galleryAddLabel,
        string galleryAddedLabel,
        string galleryRemoveLabel)
    {
        Metadata = metadata;
        ResolvedTitle = resolvedTitle;
        MetaLine = metaLine;
        ResolvedGalleryDescription = resolvedGalleryDescription;
        TagLabels = tagLabels;
        SearchBlob = searchBlob;
        IsOnDashboard = isOnDashboard;
        GalleryAddLabel = galleryAddLabel;
        GalleryAddedLabel = galleryAddedLabel;
        GalleryRemoveLabel = galleryRemoveLabel;
    }

    public WidgetMetadata Metadata { get; }
    public string ResolvedTitle { get; }
    public string MetaLine { get; }
    public string ResolvedGalleryDescription { get; }
    public IReadOnlyList<string> TagLabels { get; }
    public string SearchBlob { get; }

    /// <summary>
    /// True when this widget type is already on the overview grid.
    /// </summary>
    public bool IsOnDashboard { get; }

    public string GalleryAddLabel { get; }
    public string GalleryAddedLabel { get; }
    public string GalleryRemoveLabel { get; }
}
