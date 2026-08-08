namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:abs")]
public sealed class AbsDensityFunction : IDensityFunction
{
    public string Type => "minecraft:abs";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Math.Max(0.0, Argument.MinValue);

    public double MaxValue => Math.Max(Math.Abs(Argument.MinValue), Math.Abs(Argument.MaxValue));

    public double GetValue(double x, double y, double z) => Math.Abs(Argument.GetValue(x, y, z));
}
