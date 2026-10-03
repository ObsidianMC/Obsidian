namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the noise at (z / 4, x / 4, 0), scaled by 4. Used for z offsets.
/// </summary>
[DensityFunction("minecraft:shift_b")]
public sealed class ShiftBDensityFunction : IDensityFunction
{
    public string Type => "minecraft:shift_b";

    public required INoise Argument { get; init; }

    public double MinValue => -this.MaxValue;

    public double MaxValue => this.Argument.MaxValue * 4.0;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(z * 0.25, x * 0.25, 0.0) * 4.0;

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ShiftBDensityFunction { Argument = visitor.VisitNoise(this.Argument) });
}
