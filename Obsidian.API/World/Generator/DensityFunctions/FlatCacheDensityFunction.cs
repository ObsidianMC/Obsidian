namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:shift_x")]
[DensityFunction("minecraft:shift_z")]
[DensityFunction("minecraft:flat_cache")]
public sealed class FlatCacheDensityFunction : IDensityFunction
{
    public string Type => "minecraft:flat_cache";

    public required IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);
}
