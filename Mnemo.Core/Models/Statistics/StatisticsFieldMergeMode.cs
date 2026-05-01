namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// Controls how the field bag of an existing record is combined with the incoming write.
/// </summary>
public enum StatisticsFieldMergeMode
{
    /// <summary>
    /// Default. Merge incoming fields on top of existing fields. Fields not present on the
    /// incoming write are preserved.
    /// </summary>
    Merge = 0,

    /// <summary>
    /// Replace the field bag entirely with the incoming fields. Any existing field absent
    /// from the write is removed.
    /// </summary>
    Replace = 1
}
