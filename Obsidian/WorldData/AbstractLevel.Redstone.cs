using Obsidian.API.BlockEntities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// The level's redstone signal reads, answered by the redstone engine, and the neighbor updates plugins request.
/// </summary>
public abstract partial class AbstractLevel
{
    // Vanilla's Direction.Plane.HORIZONTAL, the directions updateNeighbourForOutputSignal looks for comparators in.
    private static readonly BlockFace[] comparatorDirections =
        [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    /// <inheritdoc/>
    public int GetSignal(Vector position, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.RedstoneEngine.GetSignal(position, direction);
    }

    /// <inheritdoc/>
    public int GetDirectSignal(Vector position, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.RedstoneEngine.GetDirectSignal(position, direction);
    }

    /// <inheritdoc/>
    public int GetDirectSignalTo(Vector position)
    {
        lock (this.Fluids.TickLock)
            return this.RedstoneEngine.GetDirectSignalTo(position);
    }

    /// <inheritdoc/>
    public bool HasSignal(Vector position, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.RedstoneEngine.GetSignal(position, direction) > 0;
    }

    /// <inheritdoc/>
    public bool HasNeighborSignal(Vector position)
    {
        lock (this.Fluids.TickLock)
            return this.RedstoneEngine.HasNeighborSignal(position);
    }

    /// <inheritdoc/>
    public int GetBestNeighborSignal(Vector position)
    {
        lock (this.Fluids.TickLock)
            return this.RedstoneEngine.GetBestNeighborSignal(position);
    }

    /// <summary>
    /// Uses a redstone block the way a player's right click does (levers, buttons, repeaters, comparators, daylight
    /// detectors). Adventure players can't change repeaters or comparators.
    /// </summary>
    /// <returns>Whether the block reacted to the use.</returns>
    internal async ValueTask<bool> UseRedstoneAsync(Vector position, IPlayer player, bool mayBuild)
    {
        IBlock? state;
        lock (this.Fluids.TickLock)
            state = ((IBlockChangeWorld)this).GetLoadedBlock(position);

        if (state is null)
            return false;

        if (!mayBuild && (state.Is(RedstoneRepeater.BlockId) || RedstoneComparator.IsComparator(state)))
            return false;

        return await BasicRedstoneComponents.TryUseAsync(this, position, state, player);
    }

    /// <summary>
    /// A projectile burst's contact with the block at <paramref name="position"/>: a lever flips and a button presses (see
    /// <see cref="BasicRedstoneComponents.TriggerAsync"/>). Returns whether the block reacted.
    /// </summary>
    internal async ValueTask<bool> TriggerRedstoneAsync(Vector position)
    {
        IBlock? state;
        lock (this.Fluids.TickLock)
            state = ((IBlockChangeWorld)this).GetLoadedBlock(position);

        return state is not null && await BasicRedstoneComponents.TriggerAsync(this, position, state);
    }

    /// <summary>
    /// The block an arrow hit reacts with: a target block takes power for a while (see <see cref="TargetBlock"/>).
    /// </summary>
    internal ValueTask OnProjectileHitBlockAsync(Vector block, VectorD location, BlockFace face) =>
        this.RedstoneInstall.Targets.HitAsync(block, location, face, TargetBlock.ArrowPulseTicks);

    /// <summary>
    /// Vanilla's <c>updateNeighbourForOutputSignal</c> for a container whose items changed: the comparators beside
    /// it, or behind a conductor beside it, hear of it and read it again. A double chest is one container to a
    /// comparator, so the comparators of its other half hear too. Remembers the revision they heard of (see
    /// <c>ComparatorRevision</c>), so that the block entity tick tells them only of a change.
    /// </summary>
    internal void UpdateComparatorsOf(ContainerBlockEntity container)
    {
        lock (this.Fluids.TickLock)
        {
            container.ComparatorRevision = container.Inventory.Revision;

            var position = container.BlockPosition;
            this.EnqueueComparatorsAround(position);

            var state = this.BlockUpdates.GetBlock(position);
            if (IsChest(state) && ChestPartner(this.BlockUpdates, position, state) is Vector partner)
                this.EnqueueComparatorsAround(partner);

            this.BlockNotifications.DrainAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Queues vanilla's <c>updateNeighbourForOutputSignal</c> reach of <paramref name="position"/>: each comparator
    /// beside it, and each one behind a redstone conductor beside it, hears that the block there changed. Doesn't
    /// drain.
    /// </summary>
    private void EnqueueComparatorsAround(Vector position)
    {
        IBlockChangeWorld world = this;
        foreach (var direction in comparatorDirections)
        {
            var beside = position.Offset(direction);
            var state = world.GetLoadedBlock(beside);
            if (state is null)
                continue;

            if (state.Is(BlockIds.Comparator))
            {
                this.BlockNotifications.EnqueueNeighbor(beside, position);
                continue;
            }

            if (!state.IsRedstoneConductor())
                continue;

            var behind = beside.Offset(direction);
            if (world.GetLoadedBlock(behind) is IBlock comparator && comparator.Is(BlockIds.Comparator))
                this.BlockNotifications.EnqueueNeighbor(behind, position);
        }
    }

    /// <inheritdoc/>
    public void UpdateNeighbors(Vector position, IBlock source)
    {
        this.BlockUpdates.UpdateNeighbors(position, source);

        // The redstone engine's reactions to the neighbors of this position.
        lock (this.Fluids.TickLock)
        {
            this.BlockNotifications.EnqueueNeighbors(position);
            this.BlockNotifications.DrainAsync().GetAwaiter().GetResult();
        }
    }
}
