using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

/// <summary>
/// Where the End's exit portal is, or is to be built: the bedrock fountain vanilla's dragon fight matches, and the
/// origin it builds a new one at.
/// </summary>
/// <remarks>
/// The portal's origin is the centre of the layer the portal blocks fill: its floor is one below, and the bedrock
/// pillar stands on the origin up to three above it.
/// </remarks>
internal static class ExitPortalSearch
{
    // Vanilla stops descending through bedrock at this height, whatever the dimension's sea level.
    private const int LowestBedrockDescent = 63;

    // The fountain's footprint around its origin: the pillar's top is three above it and the floor one below.
    private const int Reach = 3;

    // The matching orientations' offsets from the corner vanilla's pattern search starts at to the fountain's origin.
    // The fountain only matches upright, and is the same in every horizontal orientation; vanilla tries them with
    // the corner's "up" facing north, south, west and then east.
    private static readonly Vector[] uprightOrigins =
    [
        new(Reach, -Reach, Reach),
        new(-Reach, -Reach, -Reach),
        new(Reach, -Reach, -Reach),
        new(-Reach, -Reach, Reach)
    ];

    /// <summary>
    /// The first free y above the centre column's highest block <paramref name="type"/> counts, from its blocks as
    /// they are now.
    /// </summary>
    public static int Surface(Func<Vector, IBlock?> blockAt, HeightmapType type, int minY, int height)
    {
        for (var y = minY + height - 1; y >= minY; y--)
        {
            if (blockAt(new Vector(0, y, 0)) is IBlock block && WorldgenHeightmaps.Matches(type, block))
                return y + 1;
        }

        return minY;
    }

    /// <summary>
    /// Vanilla's origin for a new portal: one below the first free y of the centre's
    /// <see cref="HeightmapType.MotionBlockingNoLeaves"/> heightmap, down through bedrock while above y 63, and at
    /// least one above the bottom of the world.
    /// </summary>
    public static Vector SpawnOrigin(Func<Vector, IBlock?> blockAt, int surface, int minY)
    {
        var origin = new Vector(0, surface - 1, 0);

        while (IsBedrock(blockAt(origin)) && origin.Y > LowestBedrockDescent)
        {
            origin += Vector.Down;
        }

        return new Vector(origin.X, Math.Max(minY + 1, origin.Y), origin.Z);
    }

    /// <summary>
    /// Vanilla's search for an existing fountain: first one holding any of <paramref name="portals"/> (the end portal
    /// block entities near the island), then one reaching the centre column, from the first free y of its
    /// <see cref="HeightmapType.MotionBlocking"/> heightmap down to the bottom of the world.
    /// </summary>
    /// <returns>The found fountain's origin, or null.</returns>
    public static Vector? Find(Func<Vector, IBlock?> blockAt, IEnumerable<Vector> portals, int surface, int minY)
    {
        foreach (var portal in portals)
        {
            if (Match(blockAt, portal) is Vector origin)
                return origin;
        }

        for (var y = surface; y >= minY; y--)
        {
            if (Match(blockAt, new Vector(0, y, 0)) is Vector origin)
                return origin;
        }

        return null;
    }

    // Like vanilla's BlockPattern.find, the first fountain whose pattern corner lies in the cube from the position
    // up to six blocks along each positive axis, corners in x, then y, then z order.
    private static Vector? Match(Func<Vector, IBlock?> blockAt, Vector position)
    {
        var size = 2 * Reach;

        for (var z = 0; z <= size; z++)
        {
            for (var y = 0; y <= size; y++)
            {
                for (var x = 0; x <= size; x++)
                {
                    var corner = position + new Vector(x, y, z);

                    foreach (var offset in uprightOrigins)
                    {
                        if (IsFountain(blockAt, corner + offset))
                            return corner + offset;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the bedrock of a portal stands at <paramref name="origin"/>: the pillar on it, the rim around the
    /// portal layer and the floor below it. The other blocks can be anything.
    /// </summary>
    public static bool IsFountain(Func<Vector, IBlock?> blockAt, Vector origin)
    {
        for (var y = 0; y <= Reach; y++)
        {
            if (!IsBedrock(blockAt(origin + new Vector(0, y, 0))))
                return false;
        }

        for (var dz = -Reach; dz <= Reach; dz++)
        {
            for (var dx = -Reach; dx <= Reach; dx++)
            {
                // The rim and floor of the end_podium feature's fountain: within 2.5 of the origin below it, and
                // between 2.5 and 3.5 in its layer.
                var distance = dx * dx + dz * dz;
                var rim = distance >= 6.25 && distance < 12.25;
                var floor = distance + 1 < 6.25;

                if (rim && !IsBedrock(blockAt(origin + new Vector(dx, 0, dz))))
                    return false;

                if (floor && !IsBedrock(blockAt(origin + new Vector(dx, -1, dz))))
                    return false;
            }
        }

        return true;
    }

    private static bool IsBedrock(IBlock? block) => block is not null && block.Is(BlockIds.Bedrock);
}
