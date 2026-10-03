namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:cube")]
public sealed class CubeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:cube";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Cube(this.Argument.MinValue);

    public double MaxValue => Cube(this.Argument.MaxValue);

    public double GetValue(double x, double y, double z) => Cube(this.Argument.GetValue(x, y, z));

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new CubeDensityFunction { Argument = visitor.Map(this.Argument) });

    // Math.Pow can differ from plain multiplication in the last bit.
    internal static double Cube(double value) => value * value * value;
}
