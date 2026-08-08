namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:shifted_noise")]
public sealed class ShiftedNoiseDensityFunction : NoiseDensityFunction
{
    public override string Type => "minecraft:shifted_noise";

    public required IDensityFunction ShiftX { get; init; }
    public required IDensityFunction ShiftY { get; init; }
    public required IDensityFunction ShiftZ { get; init; }

    // The scale applies to the coordinate only; the shift is added afterwards and must not be scaled.
    public override double GetValue(double x, double y, double z) => Noise.GetValue(
        x * XzScale + ShiftX.GetValue(x, y, z),
        y * YScale + ShiftY.GetValue(x, y, z),
        z * XzScale + ShiftZ.GetValue(x, y, z));
}
