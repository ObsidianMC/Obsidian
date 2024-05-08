namespace Obsidian.API.World.Generator.DensityFunction;
public sealed class ShiftB : IDensityFunction
{
    public string Type => "minecraft:shift_b";

    public required INoise Argument { get; init; }

    public double GetValue(double x, double y, double z) => Argument.GetValue(z/4D, x/4D, 0) * 4;
}
