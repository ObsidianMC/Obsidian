namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:end_islands")]
public sealed class EndIslandsDensityFunction : IDensityFunction
{
    public string Type => "minecraft:end_islands";

    public double GetValue(double x, double y, double z) => 0.1f; //TODO: End Island Noise function
}
