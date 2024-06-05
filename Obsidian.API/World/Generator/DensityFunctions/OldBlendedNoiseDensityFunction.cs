namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:old_blended_noise")]
public class OldBlendedNoiseDensityFunction : IDensityFunction
{
    public virtual required double SmearScaleMultiplier { get; init; }

    public virtual required double XZFactor { get; init; }

    public virtual required double XZScale { get; init; }

    public virtual required double YFactor { get; init; }

    public virtual required double YScale { get; init; }

    public string Type => "minecraft:old_blended_noise";

    public virtual double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
