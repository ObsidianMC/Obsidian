namespace Obsidian.API.World.DimensionSettings;

/// <summary>
/// Represents the endpoints of a climate parameter interval.
/// </summary>
public sealed record class DimensionGeneratorValueRange
{
    /// <summary>
    /// Gets or sets the interval's minimum value.
    /// </summary>
    public float Min { get; set; }

    /// <summary>
    /// Gets or sets the interval's maximum value.
    /// </summary>
    public float Max { get; set; }
}
