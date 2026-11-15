using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Interpolates between two values along Y, clamping beyond the endpoint heights.
/// </summary>
[DensityFunction("minecraft:y_clamped_gradient")]
public sealed class YClampedGradientDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density value at the gradient's starting height.
    /// </summary>
    public required double FromValue { get; init; }
    /// <summary>
    /// Gets the starting world Y in blocks, read from <c>from_y</c>.
    /// </summary>
    public required double FromY { get; init; }
    /// <summary>
    /// Gets the density value at the gradient's ending height.
    /// </summary>
    public required double ToValue { get; init; }
    /// <summary>
    /// Gets the ending world Y in blocks, read from <c>to_y</c>.
    /// </summary>
    public required double ToY { get; init; }

    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:y_clamped_gradient";

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => Math.Min(FromValue, ToValue);

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => Math.Max(FromValue, ToValue);

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => MathUtils.ClampedMap(y, FromY, ToY, FromValue, ToValue);
}
