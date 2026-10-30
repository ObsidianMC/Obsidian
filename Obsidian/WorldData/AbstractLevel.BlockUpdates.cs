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
    /// Runs the hooks of the plugin blocks an entity touches after it ticked, like vanilla's entity movement: the
    /// <see cref="IBlockBehavior.EntityInsideAsync"/> of each block its box overlaps, and the
    /// <see cref="IBlockBehavior.SteppedOnAsync"/> of the block it stands on. Vanilla's pressure plates and wooden
    /// buttons it's inside react too.
    /// </summary>
    internal async ValueTask TouchPluginBlocksAsync(IEntity entity)
    {
        // Vanilla's checkInsideBlocks: the box shrunk by 1.0E-5, so that touching a block's face isn't being inside.
        const double inset = 1.0E-5;
        var dimension = (entity as Entities.Entity)?.Dimension ?? EntityDimension.Zero;
        var halfWidth = dimension.Width / 2.0;
        var position = entity.Position;

        var min = new Vector(
            (int)Math.Floor(position.X - halfWidth + inset),
            (int)Math.Floor(position.Y + inset),
            (int)Math.Floor(position.Z - halfWidth + inset));
        var max = new Vector(
            (int)Math.Floor(position.X + halfWidth - inset),
            (int)Math.Floor(position.Y + dimension.Height - inset),
            (int)Math.Floor(position.Z + halfWidth - inset));

        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var z = min.Z; z <= max.Z; z++)
                    await this.TouchAsync(entity, new Vector(x, y, z), inside: true);
            }
        }

        if (!entity.MovementFlags.HasFlag(MovementFlags.OnGround))
            return;

        // Vanilla's getOnPosLegacy, without its look for a fence or wall below: the block 0.2 below the feet.
        var below = new Vector(
            (int)Math.Floor(position.X),
            (int)Math.Floor(position.Y - 0.2),
            (int)Math.Floor(position.Z));

        await this.TouchAsync(entity, below, inside: false);
    }

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
    private void AfterBlockSet(Vector position, IBlock old, IBlock block, BlockUpdateFlags flags)
    {
        this.Fluids.OnBlockChanged(position, old, block, flags);
        this.NotifyBlockChange(position, old, block, flags);
    }

    /// <summary>
    /// Queues the block notification pipeline's work for a committed change, as <paramref name="flags"/> ask, and drains it now.
    /// Neighbor reactions need <see cref="BlockUpdateFlags.Neighbors"/>; shape updates run unless <see cref="BlockUpdateFlags.KnownShape"/>
    /// is set, so clients-only writes still reach observers and other shape-reacting blocks. Call under the tick lock.
    /// </summary>
    internal void NotifyBlockChange(Vector position, IBlock old, IBlock block, BlockUpdateFlags flags)
    {
        var neighbors = (flags & BlockUpdateFlags.Neighbors) != 0;
        var shapes = (flags & BlockUpdateFlags.KnownShape) == 0;
        if (old.GetHashCode() == block.GetHashCode() || (!neighbors && !shapes))
            return;

        this.BlockNotifications.Enqueue(new BlockChange(position, old, block), flags);

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

    private async ValueTask TouchAsync(IEntity entity, Vector position, bool inside)
    {
        if (this.GameEventBlock(position) is not IBlock state)
            return;

        // A vanilla class's entityInside override, found from the class that declares it.
        if (inside && entity is Entities.Entity concrete
            && BlocksRegistry.GetEntityInsideBehavior(state.RegistryId) is IEntityInsideBehavior owner)
        {
            owner.EntityInside(this.BlockUpdates, position, state, concrete);
        }

        // A redstone block's entity contact, like a detector rail's minecart press.
        if (inside && this.RedstoneRules.ContactFor(state) is IRedstoneContact contact)
            await contact.EntityInsideAsync(this, position, state);

        if (!BlockBehaviors.HasBehavior(state))
            return;

        var context = new EntityBlockContext
        {
            Entity = entity,
            Level = this,
            Position = position,
            State = state
        };

        if (inside)
            await BlockBehaviors.EntityInsideAsync(context, this.Logger);
        else
            await BlockBehaviors.SteppedOnAsync(context, this.Logger);
    }
}
