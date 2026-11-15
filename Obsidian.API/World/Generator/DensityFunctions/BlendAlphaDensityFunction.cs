namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Blending weight used when blending into pre-1.18 chunks. Always 1 without blending data, as in vanilla.
/// </summary>
[DensityFunction("minecraft:blend_alpha")]
public sealed class BlendAlphaDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:blend_alpha";

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => 0.0;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => 1.0;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => 1.0;
}
