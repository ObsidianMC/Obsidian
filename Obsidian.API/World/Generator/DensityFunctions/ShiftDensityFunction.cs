namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:shift")]
public sealed class ShiftDensityFunction : IDensityFunction
{
    public string Type => "minecraft:shift";

    public required INoise Argument
    {
        get;
        init
        {
            value.Create();
            field = value;
        }
    }

    public double MinValue => -MaxValue;

    public double MaxValue => Argument.MaxValue * 4.0;

    public double GetValue(double x, double y, double z) => Argument.GetValue(x / 4.0, y / 4.0, z / 4.0) * 4.0;
}
