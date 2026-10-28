using Obsidian.API;

namespace Obsidian.Builtins.WorldEdit;

internal readonly record struct EditRegion
{
    internal const int MaxBlocks = 32_768;
    internal const int WorldBoundary = 30_000_000;

    public Vector Min { get; }
    public Vector Max { get; }
    public Vector Size => new(Max.X - Min.X + 1, Max.Y - Min.Y + 1, Max.Z - Min.Z + 1);

    public long Volume
    {
        get
        {
            var x = (long)Max.X - Min.X + 1;
            var y = (long)Max.Y - Min.Y + 1;
            var z = (long)Max.Z - Min.Z + 1;
            return x > long.MaxValue / y || x * y > long.MaxValue / z ? long.MaxValue : x * y * z;
        }
    }

    public EditRegion(Vector first, Vector second)
    {
        Min = new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z));
        Max = new(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z));
    }

    public void CheckLimit()
    {
        if (Volume > MaxBlocks)
            throw new ArgumentException($"The region exceeds the {MaxBlocks:N0} block limit.");
        CheckPosition(Min);
        CheckPosition(Max);
    }

    internal static void CheckPosition(Vector position)
    {
        if (position.X <= -WorldBoundary || position.X >= WorldBoundary
            || position.Z <= -WorldBoundary || position.Z >= WorldBoundary)
            throw new ArgumentException("Position is outside the world boundary.");
    }

    public IEnumerable<Vector> Positions()
    {
        CheckLimit();
        for (var x = Min.X; x <= Max.X; x++)
        for (var y = (long)Min.Y; y <= Max.Y; y++)
        for (var z = Min.Z; z <= Max.Z; z++)
            yield return new Vector(x, (int)y, z);
    }

    public EditRegion Shift(Vector offset) => new(Add(Min, offset), Add(Max, offset));

    internal static Vector Add(Vector position, Vector offset) => new(
        checked(position.X + offset.X), checked(position.Y + offset.Y), checked(position.Z + offset.Z));

    internal static Vector Scale(Vector direction, int amount) => new(
        checked(direction.X * amount), checked(direction.Y * amount), checked(direction.Z * amount));
}
