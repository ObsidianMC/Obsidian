using Obsidian.API.AI;
namespace Obsidian.Entities.AI;

/// <summary>
/// Port of vanilla's <c>WalkNodeEvaluator</c> path classification. The own-state type of each block comes from
/// generated data (<see cref="PathTypeStates"/>, dumped from <c>getPathTypeFromState</c>); this class applies the
/// rules that depend on the surroundings: the above-block rule, the neighbour scan, and the mob bounding-box selection.
/// </summary>
internal static class PathTypeClassifier
{
    private const int AirStateId = 0;

    /// <summary>The own-state type of a block state, before any surroundings are considered.</summary>
    public static PathType Base(int stateId) => PathTypeStates.Get(stateId);

    /// <summary>
    /// Vanilla <c>getPathTypeStatic</c>: an air cell above a special block takes that block's danger type; otherwise an
    /// air cell above a walkable block is walkable unless a neighbour makes it dangerous.
    /// </summary>
    public static PathType Static(IPathTerrain terrain, int x, int y, int z)
    {
        var type = Base(StateAt(terrain, x, y, z) ?? AirStateId);
        if (type != PathType.OPEN)
            return type;

        // Vanilla only applies the rule when the cell below exists (y >= min build height + 1).
        if (StateAt(terrain, x, y - 1, z) is not { } below)
            return type;

        return Base(below) switch
        {
            PathType.OPEN or PathType.WATER or PathType.LAVA or PathType.WALKABLE => PathType.OPEN,
            PathType.DAMAGE_FIRE => PathType.DAMAGE_FIRE,
            PathType.DAMAGE_OTHER => PathType.DAMAGE_OTHER,
            PathType.STICKY_HONEY => PathType.STICKY_HONEY,
            PathType.POWDER_SNOW => PathType.DANGER_POWDER_SNOW,
            PathType.DAMAGE_CAUTIOUS => PathType.DAMAGE_CAUTIOUS,
            PathType.TRAPDOOR => PathType.DANGER_TRAPDOOR,
            _ => Scan(terrain, x, y, z),
        };
    }

    /// <summary>Vanilla's neighbour scan (24 cells around the node, excluding its column), first match wins.</summary>
    private static PathType Scan(IPathTerrain terrain, int x, int y, int z)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;

                    var neighbour = Base(StateAt(terrain, x + dx, y + dy, z + dz) ?? AirStateId);
                    switch (neighbour)
                    {
                        case PathType.DAMAGE_OTHER:
                            return PathType.DANGER_OTHER;
                        case PathType.DAMAGE_FIRE or PathType.LAVA:
                            return PathType.DANGER_FIRE;
                        case PathType.WATER:
                            return PathType.WATER_BORDER;
                        case PathType.DAMAGE_CAUTIOUS:
                            return PathType.DAMAGE_CAUTIOUS;
                    }
                }
            }
        }

        return PathType.WALKABLE;
    }

    /// <summary>The body's malus for a server path type.</summary>
    private static float Malus(IPathBody body, PathType type) => body.Malus(PathNodeMapping.ToPublic(type));

    /// <summary>
    /// Vanilla <c>getPathTypeOfMob</c>: classifies a node from every cell of the mob's bounding box at that position,
    /// applying door and rail rules for the mob, and returns the most restrictive type (or the highest malus).
    /// </summary>
    public static PathType ForNode(IPathBody body, Vector node)
    {
        var canOpenDoors = body.CanOpenDoors;
        var sizeX = (int)MathF.Floor(body.Dimension.Width + 1);
        var sizeY = (int)MathF.Floor(body.Dimension.Height + 1);

        Span<bool> seen = stackalloc bool[PathTypeCount];
        for (var dx = 0; dx < sizeX; dx++)
        {
            for (var dy = 0; dy < sizeY; dy++)
            {
                for (var dz = 0; dz < sizeX; dz++)
                {
                    var type = Static(body.Terrain, node.X + dx, node.Y + dy, node.Z + dz);
                    // Vanilla: a closed wooden door is walkable when the navigation can pass doors (set by
                    // setCanOpenDoors). Open doors are always passable: vanilla's blocking check reads the separate
                    // canOpenDoors flag, which defaults to true and is never cleared by entity code.
                    if (type == PathType.DOOR_WOOD_CLOSED && canOpenDoors)
                        type = PathType.WALKABLE_DOOR;

                    if (type == PathType.RAIL && !IsRailAtMob(body))
                        type = PathType.UNPASSABLE_RAIL;

                    seen[(int)type] = true;
                }
            }
        }

        if (seen[(int)PathType.FENCE])
            return PathType.FENCE;
        if (seen[(int)PathType.UNPASSABLE_RAIL])
            return PathType.UNPASSABLE_RAIL;

        var result = PathType.BLOCKED;
        for (var index = 0; index < PathTypeCount; index++)
        {
            if (!seen[index])
                continue;

            var type = (PathType)index;
            if (Malus(body, type) < 0)
                return type;
            if (Malus(body, type) >= Malus(body, result))
                result = type;
        }

        // Vanilla keeps an open node open when the mob is no wider than one block and the node itself is open.
        if (sizeX <= 1 && result != PathType.OPEN && Malus(body, result) == 0 && Static(body.Terrain, node.X, node.Y, node.Z) == PathType.OPEN)
            return PathType.OPEN;

        return result;
    }

    private static readonly int PathTypeCount = Enum.GetValues<PathType>().Length;

    // Vanilla checks the mob's own block and the block below for rails.
    private static bool IsRailAtMob(IPathBody body)
    {
        var x = (int)Math.Floor(body.Position.X);
        var y = (int)Math.Floor(body.Position.Y);
        var z = (int)Math.Floor(body.Position.Z);
        return Base(StateAt(body.Terrain, x, y, z) ?? AirStateId) == PathType.RAIL ||
            Base(StateAt(body.Terrain, x, y - 1, z) ?? AirStateId) == PathType.RAIL;
    }

    private static int? StateAt(IPathTerrain terrain, int x, int y, int z) =>
        terrain.GetBlock(new Vector(x, y, z))?.GetHashCode();
}
