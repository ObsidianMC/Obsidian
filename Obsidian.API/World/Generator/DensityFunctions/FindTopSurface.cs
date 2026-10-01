namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Scans down from <see cref="UpperBound"/> in steps of <see cref="CellHeight"/> and returns the first Y
/// where <see cref="Density"/> is positive, or <see cref="LowerBound"/> if none is found.
/// </summary>
[DensityFunction("minecraft:find_top_surface")]
public sealed class FindTopSurface : IDensityFunction
{
    public string Type => "minecraft:find_top_surface";

    public required IDensityFunction Density { get; init; }

    public required IDensityFunction UpperBound { get; init; }

    public required int LowerBound { get; init; }

    public required int CellHeight { get; init; }

    public double MinValue => this.LowerBound;

    public double MaxValue => Math.Max(this.LowerBound, this.UpperBound.MaxValue);

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

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new FindTopSurface
    {
        Density = visitor.Map(this.Density),
        UpperBound = visitor.Map(this.UpperBound),
        LowerBound = this.LowerBound,
        CellHeight = this.CellHeight
    });
}
