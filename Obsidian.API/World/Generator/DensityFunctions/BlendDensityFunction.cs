namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:blend_density")]
public sealed class BlendDensityFunction : IDensityFunction
{
    public string Type => "minecraft:blend_density";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => double.NegativeInfinity;

    public double MaxValue => double.PositiveInfinity;

    // Vanilla delegates to the chunk's Blender, which has no equivalent here, so this stays a no-op.
    public double GetValue(double x, double y, double z) => Argument.GetValue(x, y, z);
}
