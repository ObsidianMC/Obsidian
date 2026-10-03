namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:quarter_negative")]
public sealed class QuarterNegativeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:quarter_negative";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Transform(this.Argument.MinValue);

    public double MaxValue => Transform(this.Argument.MaxValue);

    public double GetValue(double x, double y, double z) => Transform(this.Argument.GetValue(x, y, z));

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new QuarterNegativeDensityFunction { Argument = visitor.Map(this.Argument) });

    internal static double Transform(double value) => value > 0.0 ? value : value * 0.25;
}
