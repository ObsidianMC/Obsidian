namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:squeeze")]
public sealed class SqueezeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:squeeze";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Transform(this.Argument.MinValue);

    public double MaxValue => Transform(this.Argument.MaxValue);

    public double GetValue(double x, double y, double z) => Transform(this.Argument.GetValue(x, y, z));

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new SqueezeDensityFunction { Argument = visitor.Map(this.Argument) });

    private static double Transform(double value)
    {
        var clamped = Math.Clamp(value, -1.0, 1.0);
        return clamped / 2.0 - clamped * clamped * clamped / 24.0;
    }
}
