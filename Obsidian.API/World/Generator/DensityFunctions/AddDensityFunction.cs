namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Adds two density function samples at the same position.
/// </summary>
[DensityFunction("minecraft:add")]
public sealed class AddDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:add";

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
    public double MinValue => this.Argument1.MinValue + this.Argument2.MinValue;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => this.Argument1.MaxValue + this.Argument2.MaxValue;

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z) => this.Argument1.GetValue(x, y, z) + this.Argument2.GetValue(x, y, z);

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new AddDensityFunction
    {
        Argument1 = visitor.Map(this.Argument1),
        Argument2 = visitor.Map(this.Argument2)
    });
}
