using Obsidian.API.World;

namespace Obsidian.WorldData.Portals;

/// <summary>A rectangular frame with optional corners; Nether uses vanilla's limits.</summary>
internal readonly record struct PortalFrame(Vector Origin, string Axis, int Width, int Height, int PortalBlocks)
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

    public static async ValueTask<PortalFrame?> FindAsync(Func<Vector, ValueTask<IBlock?>> read,
        Vector position, string axis, int minY, PortalProperties? properties = null)
    {
        properties ??= NetherProperties;
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        var bottomLimit = Math.Max(minY, position.Y - properties.MaxHeight);
        while (position.Y > bottomLimit && IsInterior(await read(position + Vector.Down), properties))
            position += Vector.Down;

        var left = await DistanceToFrameAsync(read, position, -step, properties);
        if (left < 1)
            return null;

        var origin = position - step * (left - 1);
        var width = await DistanceToFrameAsync(read, origin, step, properties);
        if (width < properties.MinWidth || width > properties.MaxWidth)
            return null;

        var portals = 0;
        for (var y = 0; y <= properties.MaxHeight; y++)
        {
            var row = origin + Vector.Up * y;
            var top = true;
            for (var x = 0; x < width; x++)
                top &= IsFrame(await read(row + step * x), properties);
            if (top)
                return y >= properties.MinHeight ? new(origin, axis, width, y, portals) : null;

            if (y == properties.MaxHeight || !IsFrame(await read(row - step), properties)
                || !IsFrame(await read(row + step * width), properties))
                return null;

            for (var x = 0; x < width; x++)
            {
                var block = await read(row + step * x);
                if (!IsInterior(block, properties))
                    return null;

                if (block!.RegistryId == properties.PortalBlock.RegistryId)
                    portals++;
            }
        }

        return null;
    }

    private static async ValueTask<int> DistanceToFrameAsync(
        Func<Vector, ValueTask<IBlock?>> read, Vector origin, Vector step, PortalProperties properties)
    {
        for (var distance = 0; distance <= properties.MaxWidth; distance++)
        {
            var position = origin + step * distance;
            var block = await read(position);
            if (!IsInterior(block, properties))
                return IsFrame(block, properties) ? distance : 0;

            if (!IsFrame(await read(position + Vector.Down), properties))
                return 0;
        }

        return 0;
    }

    private static bool IsFrame(IBlock? block, PortalProperties properties) =>
        block is not null && properties.IsFrame(block);

    private static bool IsInterior(IBlock? block, PortalProperties properties) => block is not null
        && (block.IsAir || block.RegistryId is BlockIds.Fire or BlockIds.SoulFire
            || block.RegistryId == properties.PortalBlock.RegistryId);

    internal static PortalProperties NetherProperties
    {
        get
        {
            PortalTypesRegistry.TryEnter(PortalTypesRegistry.Nether, out var properties, out _);
            return properties;
        }
    }
}
