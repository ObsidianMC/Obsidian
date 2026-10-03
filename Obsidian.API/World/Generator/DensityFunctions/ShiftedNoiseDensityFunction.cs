namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:shifted_noise")]
public sealed class ShiftedNoiseDensityFunction : IDensityFunction
{
    public string Type => "minecraft:shifted_noise";

    public required INoise Noise { get; init; }

    public required double XzScale { get; init; }

    public required double YScale { get; init; }

    public required IDensityFunction ShiftX { get; init; }
    public required IDensityFunction ShiftY { get; init; }
    public required IDensityFunction ShiftZ { get; init; }

    public double MinValue => -this.MaxValue;

    public double MaxValue => this.Noise.MaxValue;

    // Shifts are added after scaling, as in vanilla.
    public double GetValue(double x, double y, double z) => this.Noise.GetValue(
        x * this.XzScale + this.ShiftX.GetValue(x, y, z),
        y * this.YScale + this.ShiftY.GetValue(x, y, z),
        z * this.XzScale + this.ShiftZ.GetValue(x, y, z));

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new ShiftedNoiseDensityFunction
    {
        Noise = visitor.VisitNoise(this.Noise),
        XzScale = this.XzScale,
        YScale = this.YScale,
        ShiftX = visitor.Map(this.ShiftX),
        ShiftY = visitor.Map(this.ShiftY),
        ShiftZ = visitor.Map(this.ShiftZ)
    });
}
