namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:blend_alpha")]
public sealed class BlendAlphaDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_alpha";

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
