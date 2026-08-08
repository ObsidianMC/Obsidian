namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:squeeze")]
public sealed class SqueezeDensityFunction : IDensityFunction
{
    public string Type => "minecraft:squeeze";

    public required IDensityFunction Argument { get; init; }

    public double MinValue => Transform(Argument.MinValue);

    public double MaxValue => Transform(Argument.MaxValue);

    public double GetValue(double x, double y, double z) => Transform(Argument.GetValue(x, y, z));

    private static double Transform(double value)
    {
        var clamped = Math.Clamp(value, -1.0, 1.0);
        return (clamped / 2.0) - (clamped * clamped * clamped / 24.0);
    }
}
