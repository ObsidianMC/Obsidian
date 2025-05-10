namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:abs")]
public sealed class AbsDensityFunction : IDensityFunction
{
    public string Type => "minecraft:abs";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => 0;

    public double MaxValue => Argument.MaxValue;

    public double GetValue(double x, double y, double z) => Math.Abs(Argument.GetValue(x, y, z));
}
