namespace Mnemo.Core.Models.Widgets;

/// <summary>
/// Defines the position of a widget in the dashboard grid.
/// </summary>
public readonly struct WidgetPosition
{
    /// <summary>
    /// Gets the column index (0-based).
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Gets the row index (0-based).
    /// </summary>
    public int Row { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetPosition"/> struct.
    /// </summary>
    /// <param name="column">Column index (0-based).</param>
    /// <param name="row">Row index (0-based).</param>
    public WidgetPosition(int column, int row)
    {
        Column = column;
        Row = row;
    }

    /// <summary>
    /// Deconstructs the position into its components.
    /// </summary>
    public void Deconstruct(out int column, out int row)
    {
        column = Column;
        row = Row;
    }

    public override string ToString() => $"({Column}, {Row})";
}
