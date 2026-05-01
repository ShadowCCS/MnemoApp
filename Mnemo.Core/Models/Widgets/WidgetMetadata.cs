using System.Collections.Generic;

namespace Mnemo.Core.Models.Widgets;

/// <summary>
/// Contains metadata about a widget type for display in the widget gallery.
/// </summary>
public class WidgetMetadata
{
    /// <summary>
    /// Gets the unique identifier for this widget type.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display title of the widget.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the description of what this widget displays.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the category this widget belongs to.
    /// </summary>
    public WidgetCategory Category { get; }

    /// <summary>
    /// Gets the widget icon as an <c>avares://</c> URI to SVG (each widget ships <c>icon.svg</c>
    /// beside its implementation under <c>Modules/Overview/Widgets/&lt;WidgetFolder&gt;/</c>).
    /// </summary>
    public string Icon { get; }

    /// <summary>
    /// Gets the default size for this widget.
    /// </summary>
    public WidgetSize DefaultSize { get; }

    /// <summary>
    /// Gets the translation namespace for this widget (e.g. "FlashcardStats"). When set, the UI
    /// resolves <see cref="Title"/> and <see cref="Description"/> as localization keys in this namespace.
    /// When null, <see cref="Title"/> and <see cref="Description"/> are shown as literal text.
    /// </summary>
    public string? TranslationNamespace { get; }

    /// <summary>
    /// Gets the localization key under the AddWidget namespace for the product name in gallery metadata (e.g. "ProductMnemo").
    /// </summary>
    public string ProductLocalizationKey { get; }

    /// <summary>
    /// Gets the semantic version string shown in the gallery (e.g. "v1.0").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the gallery filter/category chip this widget belongs to.
    /// </summary>
    public WidgetGalleryFilterCategory GalleryFilter { get; }

    /// <summary>
    /// Gets localization keys for gallery tags, resolved in <see cref="TranslationNamespace"/> when set;
    /// otherwise each entry is shown as literal text.
    /// </summary>
    public IReadOnlyList<string> GalleryTagKeys { get; }

    /// <summary>
    /// When true, the gallery highlights this widget (e.g. recommended) with a badge and primary action styling.
    /// </summary>
    public bool IsFeatured { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetMetadata"/> class.
    /// </summary>
    /// <param name="icon"><c>avares://</c> URI to the widget's co-located <c>icon.svg</c> (Mnemo.UI).</param>
    /// <param name="translationNamespace">Optional. When set, Title and Description are used as localization keys in this namespace.</param>
    /// <param name="productLocalizationKey">Key in AddWidget namespace for the product segment of gallery metadata.</param>
    /// <param name="version">Version shown in gallery metadata.</param>
    /// <param name="galleryFilter">Filter chip / metadata category for the gallery.</param>
    /// <param name="galleryTagKeys">Optional tag keys (or literals when no translation namespace).</param>
    /// <param name="isFeatured">Whether this widget is highlighted as recommended in the gallery.</param>
    public WidgetMetadata(
        string id,
        string title,
        string description,
        WidgetCategory category,
        string icon,
        WidgetSize defaultSize,
        string? translationNamespace = null,
        string productLocalizationKey = "ProductMnemo",
        string version = "v1.0",
        WidgetGalleryFilterCategory galleryFilter = WidgetGalleryFilterCategory.Study,
        IReadOnlyList<string>? galleryTagKeys = null,
        bool isFeatured = false)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        Icon = icon;
        DefaultSize = defaultSize;
        TranslationNamespace = translationNamespace;
        ProductLocalizationKey = productLocalizationKey;
        Version = version;
        GalleryFilter = galleryFilter;
        GalleryTagKeys = galleryTagKeys ?? [];
        IsFeatured = isFeatured;
    }
}
