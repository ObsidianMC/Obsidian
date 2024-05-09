namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:noise")]
public class Noise : IDensityFunction
{
    public virtual string Type => "minecraft:noise";

    public required INoise Value { get; init; }

    public double xz_scale = 1.0f;

    public double y_scale = 1.0f;

    public virtual double GetValue(double x, double y, double z) => Value.GetValue(x * xz_scale, y * y_scale, z * xz_scale);
}
