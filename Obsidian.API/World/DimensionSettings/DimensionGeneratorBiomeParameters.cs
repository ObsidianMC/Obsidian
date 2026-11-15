namespace Obsidian.API.World.DimensionSettings;

/// <summary>
/// Holds the climate values or intervals for a multi-noise biome entry.
/// </summary>
public sealed record class DimensionGeneratorBiomeParameters
{
    /// <summary>
    /// Gets or sets this entry's climate-distance offset.
    /// </summary>
    public DimensionGeneratorParameterValue Offset { get; set; }

    /// <summary>
    /// Gets or sets the continentalness value or interval.
    /// </summary>
    public DimensionGeneratorParameterValue Continentalness { get; set; }

    /// <summary>
    /// Gets or sets the weirdness value or interval.
    /// </summary>
    public DimensionGeneratorParameterValue Weirdness { get; set; }

    /// <summary>
    /// Gets or sets the erosion value or interval.
    /// </summary>
    public DimensionGeneratorParameterValue Erosion { get; set; }

    /// <summary>
    /// Gets or sets the depth value or interval.
    /// </summary>
    public DimensionGeneratorParameterValue Depth { get; set; }

    /// <summary>
    /// Gets or sets the humidity value or interval.
    /// </summary>
    public DimensionGeneratorParameterValue Humidity { get; set; }

    /// <summary>
    /// Gets or sets the temperature value or interval.
    /// </summary>
    public DimensionGeneratorParameterValue Temperature { get; set; }
}

/// <summary>
/// Represents either a single climate value or a climate interval.
/// </summary>
public sealed record class DimensionGeneratorParameterValue
{
    /// <summary>
    /// Gets or sets the point value, or <c>null</c> when an interval is supplied.
    /// </summary>
    public float? Value { get; set; }

    /// <summary>
    /// Gets or sets the climate interval, or <c>null</c> when a point value is supplied.
    /// </summary>
    public DimensionGeneratorValueRange? Range { get; set; }
}
