namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:blend_density")]
public sealed class BlendDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_density";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => double.MinValue;

    public double MaxValue => double.MaxValue;

    public double GetValue(double x, double y, double z) => Argument.GetValue(x, y, z); // No-op
}
