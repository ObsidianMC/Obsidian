using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>LeavesBlock</c> overrides: <c>randomTick</c> (leaves that are not persistent and are too far from a
/// log decay), <c>tick</c> (the distance is recomputed) and <c>updateShape</c> (a distance change is scheduled), plus
/// its <c>getStateForPlacement</c>, which makes the leaves a player places persistent.
/// </summary>
internal sealed partial class LeavesBlockBehavior : IRandomTickBehavior, IScheduledTickBehavior, IShapeUpdateBehavior
{
    // Vanilla's LeavesBlock.DECAY_DISTANCE: the distance of leaves with no log within six blocks.
    private const int DecayDistance = 7;

    private static readonly BlockFace[] Faces = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>Vanilla's <c>LeavesBlock.randomTick</c>: decaying leaves drop their loot and leave their fluid behind. No draws.</summary>
    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!Decaying(block))
            return;

        // Re-checked when the removal runs: a log placed meanwhile keeps the leaves.
        level.QueueBlockDestruction(
            position,
            dropItems: true,
            breaker: null,
            replacement: null,
            playEffect: false,
            stillUnsupported: current => Decaying(current));

        Log.Decayed(level.Logger, position);
    }

    /// <summary>Vanilla's <c>LeavesBlock.tick</c>: the leaves take the distance their neighbours give.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random) =>
        level.SetBlock(position, WithDistance(level, position, block), BlockUpdateFlags.All);

    /// <summary>
    /// Vanilla's <c>LeavesBlock.updateShape</c>: when the neighbour's distance says the leaves should change, a tick is
    /// scheduled for a tick later. The state is kept.
    /// </summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        var expected = OptionalDistance(neighbor) + 1;
        if (expected != 1 || Distance(state) != expected)
            level.ScheduleTick(position, state, 1);

        return null;
    }

    /// <summary>
    /// Vanilla's <c>LeavesBlock.getStateForPlacement</c>: leaves placed by a player are persistent, waterlogged in
    /// source water, and take the distance their neighbours give.
    /// </summary>
    internal sealed class Placement : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var waterlogged = context.Level.GetBlock(context.Position).GetFluid() == FluidKind.Water;
            var block = context.Block
                .WithProperty("persistent", "true")
                .WithProperty("waterlogged", waterlogged ? "true" : "false");

            return WithDistance(context.Level, context.Position, block);
        }
    }

    // Vanilla's LeavesBlock.updateDistance: one more than the nearest log (0) or leaves, over the six faces, from 7.
    private static IBlock WithDistance(IBlockLevel level, Vector position, IBlock block)
    {
        var distance = DecayDistance;
        foreach (var face in Faces)
        {
            var neighbor = level.GetBlock(position + face.ToVector());
            distance = Math.Min(distance, OptionalDistance(neighbor) + 1);
            if (distance == 1)
                break;
        }

        return block.WithProperty("distance", distance.ToString());
    }

    private static int OptionalDistance(IBlock block)
    {
        // Live leaves use the current tags, including poplar and the other 26.3 leaf colors.
        if (TagsRegistry.Block.Logs.Entries.Contains(block.RegistryId))
            return 0;

        return TagsRegistry.Block.Leaves.Entries.Contains(block.RegistryId) ? Distance(block) : DecayDistance;
    }

    // Vanilla's LeavesBlock.decaying: not persistent, and as far from logs as it goes.
    private static bool Decaying(IBlock block) =>
        block.GetProperty("persistent") != "true" && Distance(block) == DecayDistance;

    private static int Distance(IBlock block) =>
        int.TryParse(block.GetProperty("distance"), out var distance) ? distance : DecayDistance;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Leaves at {Position} began to decay.")]
        public static partial void Decayed(ILogger logger, Vector position);
    }
}
