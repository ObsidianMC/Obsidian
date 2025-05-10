namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:end_islands")]
public sealed class EndIslandsDensityFunction : IDensityFunction
{
    public string Type => "minecraft:end_islands";

    public double MinValue => throw new NotImplementedException();

    public double MaxValue => throw new NotImplementedException();

    public double GetValue(double x, double y, double z) => 0.1f; //TODO: End Island Noise function
}
