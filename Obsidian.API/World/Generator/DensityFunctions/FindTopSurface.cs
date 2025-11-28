namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:find_top_surface")]
public sealed class FindTopSurface : IDensityFunction
{
    public double MinValue { get; init; }

    public double MaxValue { get; init; }

    public string Type { get; init; }

    public int CellHeight { get; init; }

    public IDensityFunction Density { get; init; }

    public IDensityFunction LowerBound { get; init; }

    public IDensityFunction UpperBound { get; init; }

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
