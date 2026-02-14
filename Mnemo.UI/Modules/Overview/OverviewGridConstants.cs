namespace Mnemo.UI.Modules.Overview;

/// <summary>
/// Shared constants for the overview dashboard grid layout.
/// Used by OverviewViewModel and grid-related converters to keep dimensions in sync.
/// </summary>
public static class OverviewGridConstants
{
    /// <summary>Number of columns in the dashboard grid.</summary>
    public const int GridColumns = 12;

    /// <summary>Width of a single grid cell in pixels.</summary>
    public const int CellWidth = 120;

    /// <summary>Height of a single grid cell in pixels.</summary>
    public const int CellHeight = 120;

    /// <summary>Spacing between grid cells in pixels.</summary>
    public const int CellSpacing = 16;
}
