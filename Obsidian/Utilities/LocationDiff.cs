namespace Obsidian.Utilities;

internal readonly struct LocationDiff
{
    public required double DifferenceX { get; init; }

    public required double DifferenceY { get; init; }

    public required double DifferenceZ { get; init; }

    public double CalculatedDifference => this.DifferenceX * this.DifferenceX + this.DifferenceZ * this.DifferenceZ;

    public static LocationDiff GetDifference(VectorD entityLocation, VectorD location) => new()
    {
        DifferenceX = entityLocation.X - location.X,
        DifferenceY = entityLocation.Y - location.Y,
        DifferenceZ = entityLocation.Z - location.Z,
    };
}
