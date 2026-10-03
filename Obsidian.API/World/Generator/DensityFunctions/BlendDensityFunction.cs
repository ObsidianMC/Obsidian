namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Blends density into pre-1.18 chunks. Without blending data this passes the argument through, as in vanilla.
/// </summary>
[DensityFunction("minecraft:blend_density")]
public sealed class BlendDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_density";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => double.NegativeInfinity;

    public double MaxValue => double.PositiveInfinity;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new BlendDensityFunction { Argument = visitor.Map(this.Argument) });
}
