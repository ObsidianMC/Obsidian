namespace Obsidian.API.World.Generator.Noise;
/// <summary>
/// Describes climate intervals and an offset for a noise settings spawn target.
/// </summary>
public readonly record struct ClimateParameter
{
    /// <summary>
    /// Gets the continentalness interval as [minimum, maximum].
    /// </summary>
    public ImmutableArray<double> Continentalness { get; init; }

    /// <summary>
    /// The depth range, [min, max], which is one value for a point.
    /// </summary>
    public ImmutableArray<double> Depth { get; init; }

    /// <summary>
    /// Gets the erosion interval as [minimum, maximum].
    /// </summary>
    public ImmutableArray<double> Erosion { get; init; }

    /// <summary>
    /// Gets the humidity interval as [minimum, maximum].
    /// </summary>
    public ImmutableArray<double> Humidity { get; init; }

    /// <summary>
    /// Gets the additional climate-distance offset for this target.
    /// </summary>
    public double Offset { get; init; }

    /// <summary>
    /// Gets the temperature interval as [minimum, maximum].
    /// </summary>
    public ImmutableArray<double> Temperature { get; init; }

    /// <summary>
    /// Gets the weirdness interval as [minimum, maximum].
    /// </summary>
    public ImmutableArray<double> Weirdness { get; init; }
}
