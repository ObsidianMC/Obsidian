namespace Obsidian.API.World.Generator.DensityFunction;

[DensityFunction("minecraft:shift")]
public sealed class ShiftDensityFunction : IDensityFunction
{
    public string Type => "minecraft:shift";

    public required INoise Argument { get; init; }

    public double GetValue(double x, double y, double z) => Argument.GetValue(x/4D, y/4D, z/4D) * 4;
}
