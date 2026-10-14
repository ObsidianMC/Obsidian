using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Plugins;

namespace EngineSample.Cable;

/// <summary>
/// Phase 4's cable, <c>sample:cable</c>: a block whose model follows its neighbours, like a fence's. It connects to
/// other cables and to the blocks that hold items (the grinder, chests, hoppers), with a boolean state property per
/// side, worked out on the server as it's placed and as its neighbours change. The client draws it from
/// <c>assets/sample/blockstates/cable.json</c>, a multipart blockstate that adds an arm for each side that connects,
/// so it needs neither a block entity nor client code.
/// </summary>
/// <remarks>
/// It only connects for now; carrying items between what it connects is for a later phase.
/// </remarks>
public sealed class CableBlock : IBlockBehavior
{
    public const string Identifier = "sample:cable";

    private static readonly BlockFace[] sides = Enum.GetValues<BlockFace>();

    /// <summary>
    /// The registered block, whose item the block's properties give it by default.
    /// </summary>
    public static BlockType Block { get; private set; } = null!;

    /// <summary>
    /// Registers the block, with a <c>north</c>, <c>south</c>, <c>east</c>, <c>west</c>, <c>up</c> and <c>down</c>
    /// property each saying whether it connects that way.
    /// </summary>
    public static void Register(IPluginRegistry registry)
    {
        Block = registry.RegisterBlock(Identifier, new BlockProperties
        {
            StateProperties = [.. sides.Select(side => StateProperty.Bool(Property(side)))],
            DefaultState = sides.ToDictionary(Property, _ => "false"),
            Hardness = 0.5f,

            // Its model is thinner than a block, so it neither hides its neighbours' faces nor blocks light.
            Opaque = false,
            Behavior = new CableBlock()
        });
    }

    /// <summary>
    /// Placed by a player, it connects to whatever around it it can.
    /// </summary>
    public IBlock? GetStateForPlacement(BlockPlaceContext context)
    {
        var state = context.Block;
        foreach (var side in sides)
            state = WithSide(state, side, ConnectsTo(context.Level, context.Position, side));

        return state;
    }

    /// <summary>
    /// A neighbour changed: the side facing it connects if the neighbour is a cable or holds items. The neighbour's
    /// own cable, if it's one, gets the same update from this side.
    /// </summary>
    public IBlock UpdateShape(ShapeUpdateContext context) =>
        WithSide(context.State, context.Direction, ConnectsTo(context.Level, context.Position, context.Direction));

    // Whether the cable at position connects to its neighbour on side: another cable, or a block whose items are
    // reached from that side, like a chest's or the grinder's.
    private static bool ConnectsTo(IBlockLevel level, Vector position, BlockFace side)
    {
        var neighbor = position + side.ToVector();

        return Block.Is(level.GetBlock(neighbor)) || level.GetItemAccess(neighbor, side.Opposite()) is not null;
    }

    private static IBlock WithSide(IBlock state, BlockFace side, bool connected) =>
        Block.With(state, Property(side), connected ? "true" : "false");

    // The state property for a side, named like vanilla's: north, south, east, west, up and down.
    private static string Property(BlockFace side) => side.ToString().ToLowerInvariant();
}
