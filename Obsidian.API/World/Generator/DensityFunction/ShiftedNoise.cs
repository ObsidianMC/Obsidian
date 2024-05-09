namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:shifted_noise")]
public sealed class ShiftedNoise : Noise
{
    public override string Type => "minecraft:shifted_noise";

    public required double ShiftX { get; init; }
    public required double ShiftY { get; init; }
    public required double ShiftZ { get; init; }

    public override double GetValue(double x, double y, double z) => base.GetValue(x + ShiftX, y + ShiftY, z + ShiftZ);
}
