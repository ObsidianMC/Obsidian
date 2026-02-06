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

    public double GetValue(double x, double y, double z)
    {
        // Find the top surface by scanning vertically from upper bound to lower bound
        // Returns the Y coordinate where the density function crosses from positive to negative

        double lowerY = LowerBound.GetValue(x, y, z);
        double upperY = UpperBound.GetValue(x, y, z);

        // Scan downward from upper bound in CellHeight increments
        int steps = (int)Math.Ceiling((upperY - lowerY) / CellHeight);

        for (int i = 0; i <= steps; i++)
        {
            double currentY = upperY - (i * CellHeight);
            if (currentY < lowerY)
                currentY = lowerY;

            double density = Density.GetValue(x, currentY, z);

            // Found the surface where density becomes positive (solid)
            if (density > 0.0)
            {
                return currentY;
            }

            if (currentY <= lowerY)
                break;
        }

        // No surface found, return lower bound
        return lowerY;
    }
}
