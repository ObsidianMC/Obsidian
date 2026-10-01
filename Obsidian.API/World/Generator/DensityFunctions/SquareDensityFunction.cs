namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:square")]
public sealed class SquareDensityFunction : IDensityFunction
{
    public string Type => "minecraft:square";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => this.Bounds.Min;

    public double MaxValue => this.Bounds.Max;

    private DensityBounds Bounds => field ??= new(Math.Max(0.0, this.Argument.MinValue),
        Math.Max(Square(this.Argument.MinValue), Square(this.Argument.MaxValue)));

    public double GetValue(double x, double y, double z) => Square(this.Argument.GetValue(x, y, z));

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new SquareDensityFunction { Argument = visitor.Map(this.Argument) });

    private static double Square(double value) => value * value;
}
