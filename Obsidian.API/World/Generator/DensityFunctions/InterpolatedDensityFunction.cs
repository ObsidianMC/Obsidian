namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Marker function. Vanilla delegates straight through to the wrapped function; the actual cell
/// interpolation happens in the noise chunk, which has no equivalent here yet.
/// </summary>
[DensityFunction("minecraft:interpolated")]
public sealed class InterpolatedDensityFunction : IDensityFunction
{
    public string Type => "minecraft:interpolated";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Argument.MinValue;

    public double MaxValue => Argument.MaxValue;

    public double GetValue(double x, double y, double z) => Argument.GetValue(x, y, z);
}
