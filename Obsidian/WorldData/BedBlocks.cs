using Obsidian.API.Blocks;
using Obsidian.Entities.AI;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// What vanilla's <c>AbstractBedBlock</c> knows about its beds, the coloured ones and straw beds: which blocks they
/// are, where a sleeper lies, and where one stands up.
/// </summary>
internal static class BedBlocks
{
    // The tops of the shapes a sleeper lies on: a bed's mattress (vanilla's column from 3 to 9 pixels), and a straw
    // bed's base (0 to 4 pixels), whose head's pillow a sleeper lies beside.
    private const double BedTop = 9 / 16.0;

    private const double StrawBedTop = 4 / 16.0;

    // Vanilla's setPosToBed: a sleeper lies an eighth of a block above the top.
    private const double AboveTop = 0.125;

    // A player's box, which a stand-up spot must fit.
    private const double PlayerHalfWidth = 0.3;

    private const double PlayerHeight = 1.8;

    /// <summary>Whether <paramref name="block"/> is one of vanilla's beds: a coloured bed or a straw bed.</summary>
    public static bool IsBed(IBlock? block) =>
        block is not null
        && (TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId) || block.Is(BlockIds.StrawBed));

    /// <summary>Which way the bed points, from its foot to its head.</summary>
    public static BlockFace Facing(IBlock bed) => FeatureHelpers.ParseFace(bed.GetProperty("facing"));

    /// <summary>
    /// Vanilla's <c>getSleepHeight</c> plus <c>setPosToBed</c>'s lift: how far above the head's block a sleeper's
    /// position is, or null where nobody can lie (a straw bed's head without its foot).
    /// </summary>
    public static double? SleepHeight(Func<Vector, IBlock?> read, Vector head, IBlock state)
    {
        if (!state.Is(BlockIds.StrawBed))
            return BedTop + AboveTop;

        // A straw bed's sleeper lies on its foot, the half without the pillow.
        if (state.GetProperty("part") == "head")
        {
            var foot = read(head.Offset(Facing(state).Opposite()));
            if (foot is null || !foot.Is(BlockIds.StrawBed) || foot.GetProperty("part") != "foot")
                return null;
        }

        return StrawBedTop + AboveTop;
    }

    /// <summary>
    /// Vanilla's <c>AbstractBedBlock.findStandUpPosition</c> for a player: the first spot around the bed whose head is
    /// at <paramref name="head"/> where a player fits on a floor, first avoiding dangerous blocks, then not; around a
    /// bunk bed, also the bed below and above it. Which side is tried first depends on <paramref name="yaw"/>.
    /// </summary>
    /// <returns>Where the player's feet go, or null when no spot fits.</returns>
    public static VectorD? StandUpPosition(ILevel level, Vector head, BlockFace forward, float yaw)
    {
        var terrain = new MobTerrain(level);
        var right = forward.ClockWise();
        var side = FacesAngle(right, yaw) ? right.Opposite() : right;

        if (IsBed(terrain.GetBlock(head + Vector.Down)))
            return BunkStandUpPosition(level, terrain, head, forward, side);

        var offsets = SurroundOffsets(forward, side).Concat(AboveOffsets(forward)).ToArray();
        return FirstSpot(level, terrain, head, offsets, avoidDanger: true)
            ?? FirstSpot(level, terrain, head, offsets, avoidDanger: false);
    }

    /// <summary>
    /// The yaw a player standing at <paramref name="spot"/> faces to look at the bed's block at <paramref name="bed"/>,
    /// like vanilla's <c>LivingEntity.stopSleeping</c>.
    /// </summary>
    public static float YawTowards(Vector bed, VectorD spot)
    {
        var look = new VectorD(bed.X + 0.5 - spot.X, bed.Y - spot.Y, bed.Z + 0.5 - spot.Z);
        var length = Math.Sqrt(look.X * look.X + look.Y * look.Y + look.Z * look.Z);
        if (length < 1e-4)
            return 0;

        var degrees = Math.Atan2(look.Z / length, look.X / length) * 180 / Math.PI - 90;
        return (float)WrapDegrees(degrees);
    }

    private static double WrapDegrees(double degrees)
    {
        var wrapped = degrees % 360;
        if (wrapped >= 180)
            wrapped -= 360;

        if (wrapped < -180)
            wrapped += 360;

        return wrapped;
    }

    // Vanilla's Direction.isFacingAngle: whether the direction points within 90 degrees of the yaw.
    private static bool FacesAngle(BlockFace direction, float yaw)
    {
        var radians = yaw * (MathF.PI / 180f);
        var dx = -MathF.Sin(radians);
        var dz = MathF.Cos(radians);
        var normal = direction.ToVector();
        return normal.X * dx + normal.Z * dz > 0f;
    }

    private static VectorD? BunkStandUpPosition(ILevel level, MobTerrain terrain, Vector head, BlockFace forward,
        BlockFace side)
    {
        var around = SurroundOffsets(forward, side);
        var above = AboveOffsets(forward);
        var below = head + Vector.Down;

        return FirstSpot(level, terrain, head, around, avoidDanger: true)
            ?? FirstSpot(level, terrain, below, around, avoidDanger: true)
            ?? FirstSpot(level, terrain, head, above, avoidDanger: true)
            ?? FirstSpot(level, terrain, head, around, avoidDanger: false)
            ?? FirstSpot(level, terrain, below, around, avoidDanger: false)
            ?? FirstSpot(level, terrain, head, above, avoidDanger: false);
    }

    // Vanilla's bedSurroundStandUpOffsets: around the bed from the chosen side's head end, along it and back.
    private static (int X, int Z)[] SurroundOffsets(BlockFace forward, BlockFace side)
    {
        var f = forward.ToVector();
        var s = side.ToVector();
        return
        [
            (s.X, s.Z),
            (s.X - f.X, s.Z - f.Z),
            (s.X - f.X * 2, s.Z - f.Z * 2),
            (-f.X * 2, -f.Z * 2),
            (-s.X - f.X * 2, -s.Z - f.Z * 2),
            (-s.X - f.X, -s.Z - f.Z),
            (-s.X, -s.Z),
            (-s.X + f.X, -s.Z + f.Z),
            (f.X, f.Z),
            (s.X + f.X, s.Z + f.Z)
        ];
    }

    // Vanilla's bedAboveStandUpOffsets: on the head, then on the foot.
    private static (int X, int Z)[] AboveOffsets(BlockFace forward)
    {
        var f = forward.ToVector();
        return [(0, 0), (-f.X, -f.Z)];
    }

    private static VectorD? FirstSpot(ILevel level, MobTerrain terrain, Vector origin, (int X, int Z)[] offsets,
        bool avoidDanger)
    {
        foreach (var (x, z) in offsets)
        {
            var spot = SafeDismountSpot(level, terrain, origin + new Vector(x, 0, z), avoidDanger);
            if (spot is VectorD found)
                return found;
        }

        return null;
    }

    // Vanilla's DismountHelper.findSafeDismountLocation for a player: on the floor of the cell (its own shape's top,
    // or a full block below it), where a player's box hits nothing and the cells aren't ones nobody spawns inside.
    private static VectorD? SafeDismountSpot(ILevel level, MobTerrain terrain, Vector cell, bool avoidDanger)
    {
        var block = terrain.GetBlock(cell);
        if (block is null || avoidDanger && IsDangerous(block))
            return null;

        var floor = FloorHeight(level, terrain, cell, block);
        if (double.IsInfinity(floor) || floor >= 1)
            return null;

        if (avoidDanger && floor <= 0 && terrain.GetBlock(cell + Vector.Down) is IBlock below && IsDangerous(below))
            return null;

        var spot = new VectorD(cell.X + 0.5, cell.Y + floor, cell.Z + 0.5);
        var box = new BoundingBox(
            spot + new VectorD(-PlayerHalfWidth, 0, -PlayerHalfWidth),
            spot + new VectorD(PlayerHalfWidth, PlayerHeight, PlayerHalfWidth));
        if (!terrain.IsFree(box))
            return null;

        var invalid = TagsRegistry.Block.InvalidSpawnInside.Entries;
        var above = terrain.GetBlock(cell + Vector.Up);
        if (invalid.Contains(block.RegistryId) || above is not null && invalid.Contains(above.RegistryId))
            return null;

        return spot;
    }

    // Vanilla's getBlockFloorHeight over non-climbable shapes: the top of the cell's own shape, else the top of a
    // shape below that reaches into it, else no floor.
    private static double FloorHeight(ILevel level, MobTerrain terrain, Vector cell, IBlock block)
    {
        var own = ShapeTop(level, cell, block);
        if (own is double top)
            return top;

        if (terrain.GetBlock(cell + Vector.Down) is not IBlock below)
            return double.NegativeInfinity;

        var belowTop = ShapeTop(level, cell + Vector.Down, below);
        return belowTop is double height && height >= 1 ? height - 1 : double.NegativeInfinity;
    }

    // Vanilla's nonClimbableShape's top: none for climbable blocks and open trapdoors, which nobody stands on.
    private static double? ShapeTop(ILevel level, Vector position, IBlock block)
    {
        if (TagsRegistry.Block.Climbable.Entries.Contains(block.RegistryId))
            return null;

        if (block.BlockClass() == "TrapDoorBlock" && block.GetProperty("open") == "true")
            return null;

        var shapes = BlockCollisionShapes.Get(level, position, block);
        return shapes.Count == 0 ? null : shapes.Max(shape => shape.Max.Y);
    }

    // Vanilla's EntityType.isBlockDangerous for a player: burning blocks, and those that hurt or trap on contact.
    private static bool IsDangerous(IBlock block)
    {
        if (TagsRegistry.Block.Fire.Entries.Contains(block.RegistryId))
            return true;

        var lit = TagsRegistry.Block.Campfires.Entries.Contains(block.RegistryId) && block.GetProperty("lit") == "true";
        return lit || block.RegistryId is BlockIds.Lava or BlockIds.MagmaBlock or BlockIds.LavaCauldron
            or BlockIds.WitherRose or BlockIds.SweetBerryBush or BlockIds.Cactus or BlockIds.PowderSnow;
    }
}
