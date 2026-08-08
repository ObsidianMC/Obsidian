using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:find_top_surface")]
public sealed class FindTopSurface : IDensityFunction
{
    public string Type => "minecraft:find_top_surface";

    public required int CellHeight { get; init; }

    public required IDensityFunction Density { get; init; }

    // Vanilla declares lower_bound as a literal int; the registry generator hands it to us as a constant.
    public required IDensityFunction LowerBound { get; init; }

    public required IDensityFunction UpperBound { get; init; }

    public double MinValue => LowerBound.MinValue;

    public double MaxValue => Math.Max(LowerBound.MaxValue, UpperBound.MaxValue);

    public double GetValue(double x, double y, double z)
    {
        var lowerY = LowerBound.GetValue(x, y, z);
        var topY = MathUtils.Floor(UpperBound.GetValue(x, y, z) / CellHeight) * CellHeight;

        if (topY <= lowerY)
            return lowerY;

        for (double blockY = topY; blockY >= lowerY; blockY -= CellHeight)
        {
            if (Density.GetValue(x, blockY, z) > 0.0)
                return blockY;
        }

        return lowerY;
    }
}
