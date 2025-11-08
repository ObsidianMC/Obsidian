namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:invert")]
public sealed class InvertDensityFunction : IDensityFunction
{
    public double MinValue { get; init; }

    public double MaxValue { get; init; }

    public string Type { get; init; }

    public IDensityFunction Argument { get; init; }

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
