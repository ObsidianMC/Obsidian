using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.WorldData.Redstone;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData;

/// <summary>
/// The level's redstone: its signals, read like the blocks are, waiting for the block updates running at the time
/// (see <see cref="LevelBlockUpdates"/>), and what drives vanilla's components from outside block updates: entities on
/// pressure plates and arrows in wooden buttons, and containers' items for comparators.
/// </summary>
public abstract partial class AbstractLevel
{
    // The inventory revision each container's comparators last heard of.
    private readonly ConditionalWeakTable<ContainerBlockEntity, StrongBox<long>> analogRevisions = [];

    /// <inheritdoc/>
    public int GetSignal(Vector position, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.GetSignal(position, direction);
    }

    /// <inheritdoc/>
    public int GetDirectSignal(Vector position, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.GetDirectSignal(position, direction);
    }

    /// <inheritdoc/>
    public int GetDirectSignalTo(Vector position)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.GetDirectSignalTo(position);
    }

    /// <inheritdoc/>
    public bool HasSignal(Vector position, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.HasSignal(position, direction);
    }

    /// <inheritdoc/>
    public bool HasNeighborSignal(Vector position)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.HasNeighborSignal(position);
    }

    /// <inheritdoc/>
    public int GetBestNeighborSignal(Vector position)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.GetBestNeighborSignal(position);
    }

    /// <inheritdoc/>
    public void UpdateNeighbors(Vector position, IBlock source) => this.BlockUpdates.UpdateNeighbors(position, source);

    /// <summary>
    /// Vanilla <c>entityInside</c> for the pressure plates and wooden buttons <paramref name="entity"/>'s box
    /// overlaps after it ticked, like vanilla's entity movement.
    /// </summary>
    private void PressRedstone(IEntity entity, Vector min, Vector max)
    {
        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var z = min.Z; z <= max.Z; z++)
                {
                    var position = new Vector(x, y, z);
                    if (this.GameEventBlock(position) is IBlock state && VanillaRedstone.IsPressedByEntities(state))
                        this.BlockUpdates.EntityInsideRedstone(position, entity);
                }
            }
        }
    }

    /// <summary>
    /// Tells the comparators reading a container whose items changed since they last heard, like vanilla's
    /// <c>setChanged</c> does when the items move.
    /// </summary>
    private void UpdateComparatorsOf(ContainerBlockEntity container)
    {
        var revision = this.analogRevisions.GetOrCreateValue(container);
        var current = container.Inventory.Revision;
        if (revision.Value == current)
            return;

        revision.Value = current;
        this.BlockUpdates.OnContainerChanged(container.BlockPosition);
    }
}
