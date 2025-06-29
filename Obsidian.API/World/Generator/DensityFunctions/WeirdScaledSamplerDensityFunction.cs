using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:weird_scaled_sampler")]
public sealed class WeirdScaledSamplerDensityFunction : IDensityFunction
{
    public string Type => "minecraft:weird_scaled_sampler";

    public required IDensityFunction Input { get; init; }

    public required string RarityValueMapper { get; init; }

    public required INoise Noise { get; init; }

    public double MinValue => 0.0;

    public double MaxValue => (RarityValueMapper == "type_1" ? 2.0 : 3.0) * Noise.MaxValue;

    public double GetValue(double x, double y, double z)
    {
        var transform = Input.GetValue(x, y, z);
        var rarityVal = RarityValueMapper == "type_1" ? MathUtils.RarityType1(transform) : MathUtils.RarityType2(transform);
        return rarityVal * Math.Abs(Noise.GetValue(x / rarityVal, y / rarityVal, z / rarityVal));
    }
}
