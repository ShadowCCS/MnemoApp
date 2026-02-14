namespace Mnemo.Core.Models.Widgets;

/// <summary>
/// Defines the size of a widget in terms of grid columns and rows.
/// </summary>
public readonly struct WidgetSize
{
    /// <summary>
    /// Gets the number of columns the widget spans.
    /// </summary>
    public int ColSpan { get; }

    /// <summary>
    /// Gets the number of rows the widget spans.
    /// </summary>
    public int RowSpan { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetSize"/> struct.
    /// </summary>
    /// <param name="colSpan">Number of columns to span.</param>
    /// <param name="rowSpan">Number of rows to span.</param>
    public WidgetSize(int colSpan, int rowSpan)
    {
        ColSpan = colSpan;
        RowSpan = rowSpan;
    }

    /// <summary>
    /// Deconstructs the widget size into its components.
    /// </summary>
    public void Deconstruct(out int colSpan, out int rowSpan)
    {
        colSpan = ColSpan;
        rowSpan = RowSpan;
    }

    public override string ToString() => $"{ColSpan}x{RowSpan}";
}
