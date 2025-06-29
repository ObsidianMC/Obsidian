namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:cache_2d")]
public sealed class Cache2DDensityFunction : IDensityFunction
{
    public string Type => "minecraft:cache_2d";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Argument.MinValue;

    public double MaxValue => Argument.MaxValue;

    public double GetValue(double x, double y, double z) => this.Argument.GetValue(x, y, z);
}
