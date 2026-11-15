namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Multiplies two density samples, skipping the second when the first is zero.
/// </summary>
[DensityFunction("minecraft:mul")]
public sealed class MulDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:mul";

    /// <summary>
    /// Gets the first density operand, read from <c>argument1</c>.
    /// </summary>
    public required IDensityFunction Argument1 { get; init; }
    /// <summary>
    /// Gets the second density operand, read from <c>argument2</c>.
    /// </summary>
    public required IDensityFunction Argument2 { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => this.Bounds.Min;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Bounds.Max;

    private DensityBounds Bounds => field ??= this.ComputeBounds();

    // The second argument is skipped when the first is zero, matching vanilla.
    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z)
    {
        var value = this.Argument1.GetValue(x, y, z);
        return value == 0.0 ? 0.0 : value * this.Argument2.GetValue(x, y, z);
    }

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new MulDensityFunction
    {
        Argument1 = visitor.Map(this.Argument1),
        Argument2 = visitor.Map(this.Argument2)
    });

    private DensityBounds ComputeBounds()
    {
        var (min1, max1, min2, max2) = (this.Argument1.MinValue, this.Argument1.MaxValue, this.Argument2.MinValue, this.Argument2.MaxValue);

        if (min1 > 0.0 && min2 > 0.0)
            return new(min1 * min2, max1 * max2);

        if (max1 < 0.0 && max2 < 0.0)
            return new(max1 * max2, min1 * min2);

        return new(Math.Min(min1 * max2, max1 * min2), Math.Max(min1 * min2, max1 * max2));
    }
}
