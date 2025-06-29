namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:shift_b")]
public sealed class ShiftBDensityFunction : IDensityFunction
{
    public string Type => "minecraft:shift_b";

    public required INoise Argument
    {
        get;
        init
        {
            value.Create();
            field = value;
        }
    }

    public double MinValue => Argument.MinValue * 4.0;

    public double MaxValue => Argument.MaxValue * 4.0;

    public double GetValue(double x, double y, double z) => Argument.GetValue(z / 4.0, x / 4.0, 0) * 4.0;
}
