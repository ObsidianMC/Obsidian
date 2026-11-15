namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Terrain offset used when blending into pre-1.18 chunks. Always 0 without blending data, as in vanilla.
/// </summary>
[DensityFunction("minecraft:blend_offset")]
public sealed class BlendOffsetDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:blend_offset";

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => double.NegativeInfinity;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => double.PositiveInfinity;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => 0.0;
}
