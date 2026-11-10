using Obsidian.API.Blocks;
using Obsidian.Plugins;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// The level's block ticks, and how plugin blocks, redstone components and falling blocks react to block updates.
    /// </summary>
    internal LevelBlockUpdates BlockUpdates => this.Fluids.Blocks;

    /// <inheritdoc/>
    public void ScheduleBlockTick(
        Vector location,
        IBlock block,
        int delay,
        TickPriority priority = TickPriority.Normal) =>
        this.BlockUpdates.ScheduleTick(location, block, delay, priority);

    /// <summary>
    /// The flags of a block change with or without block updates: <see cref="BlockUpdateFlags.All"/>, or none of the
    /// side effects (no neighbour or shape updates, and the new block isn't told it was placed).
    /// </summary>
    internal static BlockUpdateFlags UpdateFlags(bool doBlockUpdate) => doBlockUpdate
        ? BlockUpdateFlags.All
        : BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    /// <summary>
    /// Runs the side effects of a block <see cref="TrySetBlockUntrackedAsync"/> stored in place of
    /// <paramref name="old"/>, as <paramref name="flags"/> ask, like vanilla's <c>setBlock</c>: players see it, then
    /// the old block's removal, the new block's placement and its neighbours' updates run, and portals check their
    /// frames (see <see cref="FluidLevel.OnBlockChanged"/>).
    /// </summary>
    /// <remarks>
    /// Runs under the tick lock the block was stored under, so that the updates of block changes don't interleave: a
    /// change made meanwhile on another thread waits for them, and its updates follow these.
    /// </remarks>
    internal void AfterBlockSet(Vector position, IBlock old, IBlock block, BlockUpdateFlags flags)
    {
        if (this.ActiveBlockUse is BlockUseEffects effects)
        {
            effects.After(() => this.AfterBlockSet(position, old, block, flags));
            return;
        }

        this.Fluids.OnBlockChanged(position, old, block, flags);
        this.NotifyBlockChange(position, old, block, flags);
    }

    /// <summary>
    /// Queues the block notification pipeline's work for a committed change, as <paramref name="flags"/> ask, and drains it now:
    /// the redstone engine's placement reactions, and with <see cref="BlockUpdateFlags.Neighbors"/> its neighbor reactions.
    /// Call under the tick lock, after the fluid level ran the change's own updates.
    /// </summary>
    /// <remarks>
    /// The change's shape updates aren't queued: the fluid level's vanilla-ordered updater runs them, with vanilla's
    /// flags on what they write (<see cref="FluidLevel.OnBlockChanged"/>, or <see cref="FluidLevel"/>'s own writes). For
    /// a change at the top of a chain they've run when this is called; for a write nested in the chain they're queued
    /// in the updater, and run in vanilla's order after the step that wrote it. Asking the shape rules here as well
    /// converted blocks ahead of that order, stored them with every flag, and scheduled ticks vanilla doesn't.
    /// </remarks>
    internal void NotifyBlockChange(Vector position, IBlock old, IBlock block, BlockUpdateFlags flags)
    {
        if (this.ActiveBlockUse is BlockUseEffects effects)
        {
            effects.After(() => this.NotifyBlockChange(position, old, block, flags));
            return;
        }

        var neighbors = (flags & BlockUpdateFlags.Neighbors) != 0;
        var shapes = (flags & BlockUpdateFlags.KnownShape) == 0;
        if (old.GetHashCode() == block.GetHashCode() || (!neighbors && !shapes))
            return;

        this.BlockNotifications.Enqueue(new BlockChange(position, old, block), flags, shapes: false);

        // The redstone engine's reactions for the change, drained now: its writes go to loaded chunks only, so they
        // complete synchronously under the tick lock. A drain already running picks this work up.
        this.BlockNotifications.DrainAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Vanilla <c>updateNeighborsAt</c> for the redstone components around <paramref name="position"/>, and for those one block
    /// further out: their power, connections or analog output changed without a change of the block's state. Drained now,
    /// under the tick lock, as <see cref="NotifyBlockChange"/> does.
    /// </summary>
    internal void NotifyNeighborReactionsAround(Vector position)
    {
        this.BlockNotifications.EnqueueNeighbors(position);
        foreach (var face in Obsidian.WorldData.Redstone.RedstoneEngine.Directions)
            this.BlockNotifications.EnqueueNeighbors(position.Offset(face));

        this.BlockNotifications.DrainAsync().GetAwaiter().GetResult();
    }
}
