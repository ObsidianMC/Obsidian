using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private Server? owningServer;

    /// <summary>
    /// The server whose level this is, which runs the commands of its command blocks: set on a world as it's created
    /// (see <see cref="LevelFactory"/>), and a dimension's is its world's. Null for a level no server made.
    /// </summary>
    internal Server? OwningServer
    {
        get => this.owningServer ?? ((this as Dimension)?.ParentWorld as AbstractLevel)?.owningServer;
        set => this.owningServer = value;
    }

    void IRedstoneLevel.RunCommandBlock(Vector position) =>
        this.EnqueueEntityAction(() => CommandBlocks.TickAsync(this, position));

    /// <summary>
    /// Vanilla's <c>updateNeighbourForOutputSignal</c> for the block at <paramref name="position"/>: the comparators
    /// reading it hear of it now. Call holding no lock, on the level's tick.
    /// </summary>
    internal void NotifyComparatorsOf(Vector position)
    {
        lock (this.Fluids.TickLock)
        {
            this.EnqueueComparatorsAround(position);
            this.BlockNotifications.DrainAsync().GetAwaiter().GetResult();
        }
    }
}
