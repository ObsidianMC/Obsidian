using Obsidian.API.Blocks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// How one of vanilla's redstone blocks reacts to block updates and ticks, and the power it gives: the vanilla side of
/// what <see cref="IBlockBehavior"/> is for plugin blocks. <see cref="LevelBlockUpdates"/> runs them, under the
/// level's tick lock, from the same hooks plugin blocks' behaviours run from.
/// </summary>
/// <remarks>
/// Every method has the defaults of a block that does nothing, like vanilla's <c>BlockBehaviour</c>. Directions
/// follow vanilla's conventions (see <see cref="ISignalReader"/>).
/// </remarks>
internal abstract class RedstoneComponent
{
    /// <summary>
    /// Vanilla <c>isSignalSource</c>.
    /// </summary>
    public virtual bool IsSignalSource(LevelBlockUpdates level) => false;

    /// <summary>
    /// Vanilla <c>getSignal</c>: the weak power toward the block asking from <paramref name="direction"/>.
    /// </summary>
    public virtual int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) => 0;

    /// <summary>
    /// Vanilla <c>getDirectSignal</c>: the strong power toward the block asking from <paramref name="direction"/>.
    /// </summary>
    public virtual int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        0;

    /// <summary>
    /// Vanilla <c>getStateForPlacement</c>, checking that the state survives where it goes as vanilla's
    /// <c>BlockItem.canPlace</c> does.
    /// </summary>
    /// <returns>The state to place, or null to not place the block.</returns>
    public virtual IBlock? GetStateForPlacement(RedstonePlacement placement) => placement.Block;

    /// <summary>
    /// Vanilla <c>setPlacedBy</c>: after a player placed the block.
    /// </summary>
    public virtual void OnPlacedByPlayer(LevelBlockUpdates level, Vector position, IBlock state)
    {
    }

    /// <summary>
    /// Vanilla <c>onPlace</c>: the block was placed, or changed state.
    /// </summary>
    public virtual void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState)
    {
    }

    /// <summary>
    /// Vanilla <c>affectNeighborsAfterRemoval</c>: another block replaced this one, with neighbour updates.
    /// </summary>
    public virtual void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state)
    {
    }

    /// <summary>
    /// Vanilla <c>neighborChanged</c>.
    /// </summary>
    public virtual void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
    }

    /// <summary>
    /// Vanilla <c>updateShape</c>: the state after the neighbour in <paramref name="direction"/> became
    /// <paramref name="neighbor"/>; air when the block loses its support.
    /// </summary>
    public virtual IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor) => state;

    /// <summary>
    /// Vanilla <c>updateIndirectNeighbourShapes</c>.
    /// </summary>
    public virtual void UpdateIndirectNeighborShapes(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockUpdateFlags flags,
        int updateLimit)
    {
    }

    /// <summary>
    /// Vanilla <c>tick</c>, for a scheduled tick of the block.
    /// </summary>
    public virtual void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
    }

    /// <summary>
    /// Vanilla <c>useWithoutItem</c>: a player uses the block.
    /// </summary>
    /// <param name="mayBuild">Whether the player may change blocks (vanilla's <c>mayBuild</c>).</param>
    /// <returns>Whether the block handled the use.</returns>
    public virtual bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild) =>
        false;

    /// <summary>
    /// Vanilla <c>entityInside</c>: an entity's box overlaps the block's cube.
    /// </summary>
    public virtual void EntityInside(LevelBlockUpdates level, Vector position, IBlock state, IEntity entity)
    {
    }

    /// <summary>
    /// Vanilla <c>onExplosionHit</c> for an explosion that triggers blocks (a wind charge): levers flip, buttons
    /// press.
    /// </summary>
    /// <returns>Whether the block reacted.</returns>
    public virtual bool Trigger(LevelBlockUpdates level, Vector position, IBlock state) => false;
}
