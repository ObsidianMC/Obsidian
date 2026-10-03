namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the argument at cell corners and trilinearly interpolates between them during terrain fill.
/// </summary>
/// <remarks>
/// Only a marker: evaluating it directly returns the argument. Chunk generation swaps it for a
/// chunk-bound implementation through <see cref="IDensityFunction.MapAll"/>.
/// </remarks>
[DensityFunction("minecraft:interpolated")]
public sealed class InterpolatedDensityFunction : IDensityFunction
{
    public string Type => "minecraft:interpolated";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => this.Argument.MinValue;

    public double MaxValue => this.Argument.MaxValue;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new InterpolatedDensityFunction { Argument = visitor.Map(this.Argument) });
}
