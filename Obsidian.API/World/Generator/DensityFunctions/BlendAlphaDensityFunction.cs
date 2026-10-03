namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Blending weight used when blending into pre-1.18 chunks. Always 1 without blending data, as in vanilla.
/// </summary>
[DensityFunction("minecraft:blend_alpha")]
public sealed class BlendAlphaDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_alpha";

    public double MinValue => 0.0;

    public double MaxValue => 1.0;

    public double GetValue(double x, double y, double z) => 1.0;
}
