namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the noise at a quarter of the block position, scaled by 4.
/// </summary>
[DensityFunction("minecraft:shift")]
public sealed class ShiftDensityFunction : IDensityFunction
{
    public string Type => "minecraft:shift";

    public required INoise Argument { get; init; }

    public double MinValue => -this.MaxValue;

    public double MaxValue => this.Argument.MaxValue * 4.0;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x * 0.25, y * 0.25, z * 0.25) * 4.0;

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ShiftDensityFunction { Argument = visitor.VisitNoise(this.Argument) });
}
