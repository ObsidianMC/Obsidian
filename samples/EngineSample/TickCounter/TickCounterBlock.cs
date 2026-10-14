using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Plugins;

namespace EngineSample.TickCounter;

/// <summary>
/// Phase 1's block, <c>sample:tick_counter</c>: it faces the player who places it, turns on and lights up while
/// redstone powers it, and its block entity (<see cref="TickCounterBlockEntity"/>) counts the ticks it's on, which
/// comparators read. Breaking it drops it through its loot table,
/// <c>data/sample/loot_table/blocks/tick_counter.json</c>.
/// </summary>
public sealed class TickCounterBlock : IBlockBehavior
{
    public const string Identifier = "sample:tick_counter";

    // The game ticks it takes to follow a change of the power it gets, as a scheduled tick.
    private const int PowerDelay = 2;

    // The light level it gives off while powered.
    private const int PoweredLight = 10;

    /// <summary>
    /// The registered block. A static of the plugin's own: a reload loads a new copy of the plugin, which registers
    /// the block again and sets its own.
    /// </summary>
    public static BlockType Block { get; private set; } = null!;

    /// <summary>
    /// Registers the block, its item (which the block's properties give it by default) and its block entity type.
    /// </summary>
    public static void Register(IPluginRegistry registry)
    {
        Block = registry.RegisterBlock(Identifier, new BlockProperties
        {
            StateProperties =
            [
                StateProperty.Enum("facing", "north", "east", "south", "west"),
                StateProperty.Bool("powered")
            ],
            DefaultState = new Dictionary<string, string> { ["powered"] = "false" },
            Hardness = 1,

            // Its block entity gives it a shape lower than a full block, so it lets light through, like glass.
            Opaque = false,

            // The runtime light engine relights the area whenever the block changes to a state with another level.
            StateLightEmission = values => values["powered"] == "true" ? PoweredLight : 0,
            Behavior = new TickCounterBlock()
        });

        registry.RegisterBlockEntity<TickCounterBlockEntity>(Identifier, Block);
    }

    /// <summary>
    /// Whether <paramref name="state"/> is a powered state of the block.
    /// </summary>
    public static bool IsPowered(IBlock state) => Block.Is(state) && Block.GetValue(state, "powered") == "true";

    /// <summary>
    /// Faces the player placing it, and starts powered when it's placed beside power.
    /// </summary>
    public IBlock? GetStateForPlacement(BlockPlaceContext context)
    {
        var facing = context.HorizontalDirection.Opposite().ToString().ToLowerInvariant();
        var powered = context.Level.HasNeighborSignal(context.Position);

        return WithPowered(Block.With(context.Block, "facing", facing), powered);
    }

    /// <summary>
    /// The power around it may have changed: it follows on a scheduled tick, unless one is scheduled already.
    /// </summary>
    public void NeighborChanged(NeighborChangedContext context)
    {
        if (context.Level.HasNeighborSignal(context.Position) != IsPowered(context.State))
            context.Level.ScheduleTick(context.Position, context.State, PowerDelay);
    }

    /// <summary>
    /// Turns on or off with the power it gets now. The block entity stays: the block keeps its type.
    /// </summary>
    public void Tick(BlockContext context)
    {
        var powered = context.Level.HasNeighborSignal(context.Position);

        if (powered != IsPowered(context.State))
            context.Level.SetBlock(context.Position, WithPowered(context.State, powered));
    }

    public bool HasAnalogOutput(IBlock state) => true;

    /// <summary>
    /// What a comparator reads from it: the seconds its block entity counted, up to 15.
    /// </summary>
    public int GetAnalogOutput(SignalContext context) =>
        context.Level.GetBlockEntity(context.Position) is TickCounterBlockEntity counter ? counter.ComparatorOutput : 0;

    private static IBlock WithPowered(IBlock state, bool powered) =>
        Block.With(state, "powered", powered ? "true" : "false");
}
