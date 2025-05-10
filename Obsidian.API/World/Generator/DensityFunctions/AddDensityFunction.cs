namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:add")]
[DensityFunction("minecraft:overworld_large_biomes/sloped_cheese")]
public sealed class AddDensityFunction : IDensityFunction
{
    public string Type => "minecraft:add";

    public required IDensityFunction Argument1 { get; init; }

    public required IDensityFunction Argument2 { get; init; }

    public double MinValue => Math.Min(Argument1.MinValue, Argument2.MinValue);

    public double MaxValue => Math.Max(Argument1.MaxValue, Argument2.MaxValue);

    public double GetValue(double x, double y, double z) => Argument1.GetValue(x, y, z) + Argument2.GetValue(x, y, z);
}
