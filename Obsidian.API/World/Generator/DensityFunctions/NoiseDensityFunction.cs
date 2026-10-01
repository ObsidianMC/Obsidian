namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:noise")]
public sealed class NoiseDensityFunction : IDensityFunction
{
    public string Type => "minecraft:noise";

    public required INoise Noise { get; init; }

    public required double XzScale { get; init; }

    public required double YScale { get; init; }

    public double MinValue => -this.MaxValue;

    public double MaxValue => this.Noise.MaxValue;

    public double GetValue(double x, double y, double z) => this.Noise.GetValue(x * this.XzScale, y * this.YScale, z * this.XzScale);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new NoiseDensityFunction
    {
        Noise = visitor.VisitNoise(this.Noise),
        XzScale = this.XzScale,
        YScale = this.YScale
    });
}
