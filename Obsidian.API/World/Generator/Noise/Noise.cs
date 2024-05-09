namespace Obsidian.API.World.Generator.Noise;

[DensityFunction("minecraft:noise")]
public class Noise : IDensityFunction
{
    public virtual string Type => "minecraft:noise";

    public required INoise Value { get; init; }

    public required double XZScale { get; set; }

    public required double YScale { get; set; }

    public virtual double GetValue(double x, double y, double z) => Value.GetValue(x * XZScale, y * YScale, z * XZScale);
}
