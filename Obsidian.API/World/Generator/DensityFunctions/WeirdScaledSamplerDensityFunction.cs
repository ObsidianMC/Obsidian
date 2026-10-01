using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:weird_scaled_sampler")]
public sealed class WeirdScaledSamplerDensityFunction : IDensityFunction
{
    public string Type => "minecraft:weird_scaled_sampler";

    public required IDensityFunction Input { get; init; }

    /// <summary>
    /// <c>type_1</c> (3D spaghetti caves) or <c>type_2</c> (2D spaghetti caves).
    /// </summary>
    public required string RarityValueMapper { get; init; }

    public required INoise Noise { get; init; }

    public double MinValue => 0.0;

    public double MaxValue => (this.IsType1 ? 2.0 : 3.0) * this.Noise.MaxValue;

    private bool IsType1 => this.RarityValueMapper == "type_1";

    public double GetValue(double x, double y, double z)
    {
        var input = this.Input.GetValue(x, y, z);
        var rarity = this.IsType1 ? MathUtils.RarityType1(input) : MathUtils.RarityType2(input);
        return rarity * Math.Abs(this.Noise.GetValue(x / rarity, y / rarity, z / rarity));
    }

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new WeirdScaledSamplerDensityFunction
    {
        Input = visitor.Map(this.Input),
        RarityValueMapper = this.RarityValueMapper,
        Noise = visitor.VisitNoise(this.Noise)
    });
}
