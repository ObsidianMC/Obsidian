using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Scales absolute noise samples by a rarity selected from an input density.
/// </summary>
[DensityFunction("minecraft:weird_scaled_sampler")]
public sealed class WeirdScaledSamplerDensityFunction : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:weird_scaled_sampler";

    /// <summary>
    /// Gets the density function used to select the rarity scale.
    /// </summary>
    public required IDensityFunction Input { get; init; }

    /// <summary>
    /// <c>type_1</c> (3D spaghetti caves) or <c>type_2</c> (2D spaghetti caves).
    /// </summary>
    public required string RarityValueMapper { get; init; }

    /// <summary>
    /// Gets the noise sampled by this function, read from <c>noise</c>.
    /// </summary>
    public required INoise Noise { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => 0.0;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => (this.IsType1 ? 2.0 : 3.0) * this.Noise.MaxValue;

    private bool IsType1 => this.RarityValueMapper == "type_1";

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z)
    {
        var input = this.Input.GetValue(x, y, z);
        var rarity = this.IsType1 ? MathUtils.RarityType1(input) : MathUtils.RarityType2(input);
        return rarity * Math.Abs(this.Noise.GetValue(x / rarity, y / rarity, z / rarity));
    }

    /// <summary>
    /// Maps child functions and noises with the visitor, then applies it to the rebuilt function.
    /// </summary>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new WeirdScaledSamplerDensityFunction
    {
        Input = visitor.Map(this.Input),
        RarityValueMapper = this.RarityValueMapper,
        Noise = visitor.VisitNoise(this.Noise)
    });
}
