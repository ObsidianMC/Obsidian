namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Terrain offset used when blending into pre-1.18 chunks. Always 0 without blending data, as in vanilla.
/// </summary>
[DensityFunction("minecraft:blend_offset")]
public sealed class BlendOffsetDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_offset";

    public double MinValue => double.NegativeInfinity;

    public double MaxValue => double.PositiveInfinity;

    public double GetValue(double x, double y, double z) => 0.0;
}
