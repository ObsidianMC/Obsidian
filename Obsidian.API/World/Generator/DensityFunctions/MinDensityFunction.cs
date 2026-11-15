namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Returns the smaller of two density samples, skipping the second when its bounds allow.
/// </summary>
[DensityFunction("minecraft:min")]
public sealed class MinDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:min";

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
    public double MinValue => Math.Min(this.Argument1.MinValue, this.Argument2.MinValue);

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => Math.Min(this.Argument1.MaxValue, this.Argument2.MaxValue);

    private DensityBounds Argument2Bounds => field ??= new(this.Argument2.MinValue, this.Argument2.MaxValue);

    // The second argument is skipped when it cannot go lower than the first, matching vanilla.
    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z)
    {
        var value = this.Argument1.GetValue(x, y, z);
        return value < this.Argument2Bounds.Min ? value : Math.Min(value, this.Argument2.GetValue(x, y, z));
    }

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new MinDensityFunction
    {
        Argument1 = visitor.Map(this.Argument1),
        Argument2 = visitor.Map(this.Argument2)
    });
}
