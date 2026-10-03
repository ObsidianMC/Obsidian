namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Samples the noise at (x / 4, 0, z / 4), scaled by 4. Used for x offsets.
/// </summary>
[DensityFunction("minecraft:shift_a")]
public sealed class ShiftADensityFunction : IDensityFunction
{
    public string Type => "minecraft:shift_a";

    public required INoise Argument { get; init; }

    public double MinValue => -this.MaxValue;

    public double MaxValue => this.Argument.MaxValue * 4.0;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x * 0.25, 0.0, z * 0.25) * 4.0;

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ShiftADensityFunction { Argument = visitor.VisitNoise(this.Argument) });
}
