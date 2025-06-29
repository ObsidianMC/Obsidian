namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:blend_offset")]
public sealed class BlendOffsetDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_offset";

    public double MinValue => double.MinValue;

    public double MaxValue => double.MaxValue;

    public double GetValue(double x, double y, double z) => 1.0;
}
