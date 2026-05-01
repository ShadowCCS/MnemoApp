namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// The supported scalar types for a <see cref="StatValue"/>. Restricted set to keep storage,
/// JSON serialization, and validation predictable for both internal modules and extensions.
/// </summary>
public enum StatValueType
{
    Boolean = 0,
    Integer = 1,
    Decimal = 2,
    String = 3,
    DateTime = 4
}
