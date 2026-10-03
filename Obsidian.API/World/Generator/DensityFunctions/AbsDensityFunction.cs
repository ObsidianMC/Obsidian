namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:abs")]
public sealed class AbsDensityFunction : IDensityFunction
{
    public string Type => "minecraft:abs";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => this.Bounds.Min;

    public double MaxValue => this.Bounds.Max;

    private DensityBounds Bounds => field ??= new(Math.Max(0.0, this.Argument.MinValue),
        Math.Max(Math.Abs(this.Argument.MinValue), Math.Abs(this.Argument.MaxValue)));

    public double GetValue(double x, double y, double z) => Math.Abs(this.Argument.GetValue(x, y, z));

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new AbsDensityFunction { Argument = visitor.Map(this.Argument) });
}
