namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Returns the same density value at every position.
/// </summary>
[DensityFunction("minecraft:constant")]
public sealed class ConstantDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:constant";

    /// <summary>
    /// Gets the constant density returned at every position.
    /// </summary>
    public required double Argument { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => Argument;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => Argument;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => Argument;
}
