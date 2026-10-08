namespace Obsidian.WorldData.Portals;

/// <summary>Vanilla PortalShape: an obsidian rectangle, with optional corners and a 2–21 by 3–21 interior.</summary>
internal readonly record struct NetherPortalShape(Vector Origin, string Axis, int Width, int Height, int PortalBlocks)
{
    internal const int MaxSize = 21;
    public Vector Step => Axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
    public bool IsComplete => PortalBlocks == Width * Height;

    public IEnumerable<Vector> Interior()
    {
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                yield return Origin + Step * x + Vector.Up * y;
    }

    public static async ValueTask<NetherPortalShape?> FindAsync(Func<Vector, ValueTask<IBlock?>> read,
        Vector position, string axis, int minY)
    {
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        var bottomLimit = Math.Max(minY, position.Y - MaxSize);
        while (position.Y > bottomLimit && IsInterior(await read(position + Vector.Down)))
            position += Vector.Down;

        var left = await DistanceToFrameAsync(read, position, -step);
        if (left < 1)
            return null;
        var origin = position - step * (left - 1);
        var width = await DistanceToFrameAsync(read, origin, step);
        if (width is < 2 or > MaxSize)
            return null;

        var portals = 0;
        for (var y = 0; y <= MaxSize; y++)
        {
            var row = origin + Vector.Up * y;
            var top = true;
            for (var x = 0; x < width; x++)
                top &= (await read(row + step * x))?.Material == Material.Obsidian;
            if (top)
                return y >= 3 ? new(origin, axis, width, y, portals) : null;
            if (y == MaxSize || (await read(row - step))?.Material != Material.Obsidian ||
                (await read(row + step * width))?.Material != Material.Obsidian)
                return null;
            for (var x = 0; x < width; x++)
            {
                var block = await read(row + step * x);
                if (!IsInterior(block))
                    return null;
                if (block!.Material == Material.NetherPortal)
                    portals++;
            }
        }
        return null;
    }

    private static async ValueTask<int> DistanceToFrameAsync(Func<Vector, ValueTask<IBlock?>> read, Vector origin, Vector step)
    {
        for (var distance = 0; distance <= MaxSize; distance++)
        {
            var position = origin + step * distance;
            var block = await read(position);
            if (!IsInterior(block))
                return block?.Material == Material.Obsidian ? distance : 0;
            if ((await read(position + Vector.Down))?.Material != Material.Obsidian)
                return 0;
        }
        return 0;
    }

    private static bool IsInterior(IBlock? block) => block is not null &&
        (block.IsAir || block.Material is Material.Fire or Material.SoulFire or Material.NetherPortal);
}
