namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Scans down from <see cref="UpperBound"/> in steps of <see cref="CellHeight"/> and returns the first Y
/// where <see cref="Density"/> is positive, or <see cref="LowerBound"/> if none is found.
/// </summary>
[DensityFunction("minecraft:find_top_surface")]
public sealed class FindTopSurface : IDensityFunction
{
    /// <summary>
    /// Gets the density function's codec type identifier.
    /// </summary>
    public string Type => "minecraft:find_top_surface";

    /// <summary>
    /// Gets the density function whose positive samples identify a surface.
    /// </summary>
    public required IDensityFunction Density { get; init; }

    /// <summary>
    /// Gets the function giving the starting world Y, rounded down to a cell boundary.
    /// </summary>
    public required IDensityFunction UpperBound { get; init; }

    /// <summary>
    /// Gets the lowest world Y to test and the fallback height when no positive sample is found.
    /// </summary>
    public required int LowerBound { get; init; }

    /// <summary>
    /// Gets the positive downward sampling step, in blocks, read from <c>cell_height</c>.
    /// </summary>
    public required int CellHeight { get; init; }

    /// <summary>
    /// Gets the lower bound reported for this function's output.
    /// </summary>
    public double MinValue => this.LowerBound;

    /// <summary>
    /// Gets the upper bound reported for this function's output.
    /// </summary>
    public double MaxValue => Math.Max(this.LowerBound, this.UpperBound.MaxValue);

    /// <summary>
    /// Evaluates this function at the supplied world block coordinates.
    /// </summary>
    public double GetValue(double x, double y, double z)
    {
        var top = (int)Math.Floor(this.UpperBound.GetValue(x, y, z) / this.CellHeight) * this.CellHeight;
        if (top <= this.LowerBound)
            return this.LowerBound;

        for (var currentY = top; currentY >= this.LowerBound; currentY -= this.CellHeight)
        {
            if (this.Density.GetValue(x, currentY, z) > 0.0)
                return currentY;
        }

        return this.LowerBound;
    }

    /// <inheritdoc/>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new FindTopSurface
    {
        Density = visitor.Map(this.Density),
        UpperBound = visitor.Map(this.UpperBound),
        LowerBound = this.LowerBound,
        CellHeight = this.CellHeight
    });
}
