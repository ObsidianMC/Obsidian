using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class ShiftedNoise : Noise
{
    public new string Type => "minecraft:shifted_noise";

    public required double shift_x;
    public required double shift_y;
    public required double shift_z;

    public double GetValue(double x, double y, double z) => base.GetValue(x + shift_x, y + shift_y, z + shift_z);
}
