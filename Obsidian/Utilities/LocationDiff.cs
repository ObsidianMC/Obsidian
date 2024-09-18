namespace Obsidian.Utilities;

internal readonly struct LocationDiff
{
    public required float DifferenceX { get; init; }

    public required float DifferenceY { get; init; }

    public required float DifferenceZ { get; init; }

    public float CalculatedDifference => this.DifferenceX * this.DifferenceX + this.DifferenceZ * this.DifferenceZ;

    public static LocationDiff GetDifference(VectorF entityLocation, VectorF location) => new()
    {
        DifferenceX = entityLocation.X - location.X,
        DifferenceY = entityLocation.Y - location.Y,
        DifferenceZ = entityLocation.Z - location.Z,
    };
}
