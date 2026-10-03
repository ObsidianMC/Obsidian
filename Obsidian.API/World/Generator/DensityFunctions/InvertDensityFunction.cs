namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:invert")]
public sealed class InvertDensityFunction : IDensityFunction
{
    public string Type => "minecraft:invert";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => this.Bounds.Min;

    public double MaxValue => this.Bounds.Max;

    // An input range spanning zero makes the output unbounded.
    private DensityBounds Bounds => field ??= this.Argument.MinValue < 0.0 && this.Argument.MaxValue > 0.0
        ? new(double.NegativeInfinity, double.PositiveInfinity)
        : new(1.0 / this.Argument.MaxValue, 1.0 / this.Argument.MinValue);

    public double GetValue(double x, double y, double z) => 1.0 / this.Argument.GetValue(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new InvertDensityFunction { Argument = visitor.Map(this.Argument) });
}
