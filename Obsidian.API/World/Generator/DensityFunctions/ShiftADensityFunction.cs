namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:shift_a")]
public sealed class ShiftADensityFunction : IDensityFunction
{
    public string Type => "minecraft:shift_a";

    public required INoise Argument {
        get;
        init
        {
            value.Create();
            field = value;
        }
    }

    public double MinValue => Argument.MinValue * 4.0;

    public double MaxValue => Argument.MaxValue * 4.0;

    public double GetValue(double x, double y, double z) => Argument.GetValue(x / 4.0, 0, z / 4.0) * 4.0;
}
