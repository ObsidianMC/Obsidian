using Obsidian.API.BlockEntities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// The level's redstone signal reads, answered by the redstone engine, and the neighbor updates plugins request.
/// </summary>
public abstract partial class AbstractLevel
{
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
            return this.RedstoneEngine.HasSignal(position, direction);
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

        return await BasicRedstoneComponents.TryUseAsync(this, position, state);
    }

    /// <summary>
    /// The block an arrow hit reacts with: a target block takes power for a while (see <see cref="TargetBlock"/>).
    /// </summary>
    internal ValueTask OnProjectileHitBlockAsync(Vector block, VectorD location, BlockFace face) =>
        this.RedstoneInstall.Targets.HitAsync(block, location, face, TargetBlock.ArrowPulseTicks);

    /// <summary>
    /// Tells the neighbors of a container whose items changed. Comparators don't read containers in the redstone engine yet,
    /// so this only queues the neighbor reactions.
    /// </summary>
    internal void UpdateComparatorsOf(ContainerBlockEntity container)
    {
        lock (this.Fluids.TickLock)
        {
            this.BlockNotifications.EnqueueNeighbors(container.BlockPosition);
            this.BlockNotifications.DrainAsync().GetAwaiter().GetResult();
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
