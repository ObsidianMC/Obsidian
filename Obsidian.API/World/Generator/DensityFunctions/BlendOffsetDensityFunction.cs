namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:blend_offset")]
public sealed class BlendOffsetDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_offset";

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
