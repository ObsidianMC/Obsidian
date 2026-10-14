using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Which sides redstone dust connects to, kept in its <c>north</c>, <c>east</c>, <c>south</c> and <c>west</c>
/// properties: <c>side</c> along the ground, <c>up</c> climbing the neighbour, or <c>none</c>. Clients draw the dust,
/// and the dust powers, by these, so the rules are vanilla's.
/// </summary>
/// <remarks>
/// Dust connects to dust beside it, or on top of or below the block beside it, and to signal sources beside it.
/// Dust connected on one axis only extends across it; dust connected nowhere is a cross, unless it was a dot (a
/// player's choice) and still connects nowhere.
/// </remarks>
internal static class WireConnections
{
    // The sides in vanilla's Direction.Plane.HORIZONTAL order.
    private static readonly BlockFace[] sides = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    /// <summary>
    /// Dust connected nowhere, without power.
    /// </summary>
    public static IBlock Dot { get; } = BlocksRegistry.RedstoneWire;

    /// <summary>
    /// Dust connected along the ground on every side, without power.
    /// </summary>
    public static IBlock Cross { get; } = sides.Aggregate(Dot, (state, side) => state.WithProperty(Name(side), "side"));

    /// <summary>
    /// The dust's power, from 0 to 15.
    /// </summary>
    public static int Power(IBlock wire) => int.Parse(wire.GetProperty("power")!);

    /// <summary>
    /// Whether dust stands on the block below: on a sturdy top face, or on a hopper.
    /// </summary>
    public static bool CanStandOn(IBlock below) => below.IsFaceSturdy(BlockFace.Up) || below.Is(BlockIds.Hopper);

    /// <summary>
    /// Whether dust connects toward a side, along the ground or climbing.
    /// </summary>
    public static bool IsConnected(IBlock wire, BlockFace side) => wire.GetProperty(Name(side)) != "none";

    /// <summary>
    /// Whether dust connects nowhere.
    /// </summary>
    public static bool IsDot(IBlock wire) => sides.All(side => !IsConnected(wire, side));

    /// <summary>
    /// Whether dust connects everywhere.
    /// </summary>
    public static bool IsCross(IBlock wire) => sides.All(side => IsConnected(wire, side));

    /// <summary>
    /// <paramref name="wire"/>, with its power, connected to every side it can at <paramref name="position"/>. A dot
    /// stays a dot while it connects nowhere.
    /// </summary>
    public static IBlock Connect(WireView view, IBlock wire, Vector position)
    {
        var canClimb = !view.GetBlock(position + Vector.Up).IsRedstoneConductor();
        var connections = new string[sides.Length];
        for (var index = 0; index < sides.Length; index++)
            connections[index] = SideTowards(view, position, sides[index], canClimb);

        var alongZ = connections[0] != "none" || connections[2] != "none";
        var alongX = connections[1] != "none" || connections[3] != "none";
        var keepsDot = IsDot(wire) && !alongZ && !alongX;

        var connected = Dot.WithProperty("power", wire.GetProperty("power")!);
        for (var index = 0; index < sides.Length; index++)
        {
            // Unless kept a dot, dust reaches across an axis it has no connection on.
            var extends = !keepsDot && (sides[index].IsAlongZ() ? !alongX : !alongZ);
            var side = connections[index] == "none" && extends ? "side" : connections[index];
            connected = connected.WithProperty(Name(sides[index]), side);
        }

        return connected;
    }

    /// <summary>
    /// <paramref name="wire"/> after its neighbour in <paramref name="direction"/> became <paramref name="neighbor"/>:
    /// air without a block to stand on; otherwise its connections follow.
    /// </summary>
    /// <remarks>
    /// Like vanilla's, a change beside it that doesn't make or break a connection only changes how that side
    /// connects (along the ground or climbing), so dust extended across an axis stays so; any other change works its
    /// connections out again as if it was a cross.
    /// </remarks>
    public static IBlock Reconnect(WireView view, IBlock wire, Vector position, BlockFace direction, IBlock neighbor)
    {
        if (direction == BlockFace.Down)
            return CanStandOn(neighbor) ? wire : BlocksRegistry.Air;

        if (direction == BlockFace.Up)
            return Connect(view, wire, position);

        var canClimb = !view.GetBlock(position + Vector.Up).IsRedstoneConductor();
        var side = SideTowards(view, position, direction, canClimb);
        if ((side != "none") == IsConnected(wire, direction) && !IsCross(wire))
            return wire.WithProperty(Name(direction), side);

        return Connect(view, Cross.WithProperty("power", wire.GetProperty("power")!), position);
    }

    private static string Name(BlockFace side) => side.PropertyName();

    private static bool IsAlongZ(this BlockFace side) => side is BlockFace.North or BlockFace.South;

    // How dust connects toward a side: climbing to dust on top of the neighbour (along the ground when the neighbour
    // has no full face toward it), along the ground to dust or a source beside it or to dust below the neighbour, or
    // not at all.
    private static string SideTowards(WireView view, Vector position, BlockFace direction, bool canClimb)
    {
        var neighborPosition = position.Offset(direction);
        var neighbor = view.GetBlock(neighborPosition);
        if (canClimb)
        {
            var climbable = neighbor.BlockClass() is "TrapDoorBlock" or "WeatheringCopperTrapDoorBlock"
                || CanStandOn(neighbor);

            if (climbable && Joins(view, view.GetBlock(neighborPosition + Vector.Up), beside: null))
                return neighbor.IsFaceSturdy(direction.Opposite()) ? "up" : "side";
        }

        var joins = Joins(view, neighbor, direction)
            || !neighbor.IsRedstoneConductor() && Joins(view, view.GetBlock(neighborPosition + Vector.Down), null);

        return joins ? "side" : "none";
    }

    // Whether dust joins a block: other dust always; beside it (in direction beside), repeaters along their axis,
    // observers from their face, and any other signal source.
    private static bool Joins(WireView view, IBlock block, BlockFace? beside)
    {
        if (block.Is(BlockIds.RedstoneWire))
            return true;

        if (beside is not BlockFace side)
            return false;

        if (block.Is(BlockIds.Repeater))
            return FeatureHelpers.SameAxis(FeatureHelpers.ParseFace(block.GetProperty("facing")), side);

        if (block.Is(BlockIds.Observer))
            return FeatureHelpers.ParseFace(block.GetProperty("facing")) == side;

        return view.IsSignalSource(block);
    }
}

/// <summary>
/// What redstone dust's connections read: the blocks around, and which of them are signal sources (which plugin
/// blocks answer too on a live level).
/// </summary>
internal readonly record struct WireView(Func<Vector, IBlock> GetBlock, Func<IBlock, bool> IsSignalSource);
