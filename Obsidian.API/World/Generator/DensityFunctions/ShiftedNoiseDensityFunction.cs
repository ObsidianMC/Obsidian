namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:shifted_noise")]
public sealed class ShiftedNoiseDensityFunction : NoiseDensityFunction
{
    public override string Type => "minecraft:shifted_noise";

    public required IDensityFunction ShiftX { get; init; }
    public required IDensityFunction ShiftY { get; init; }
    public required IDensityFunction ShiftZ { get; init; }

    public override double GetValue(double x, double y, double z) => base.GetValue(
        x + ShiftX.GetValue(x, y, z),
        y + ShiftY.GetValue(x, y, z),
        z + ShiftZ.GetValue(x, y, z));
}
