using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// What redstone behaviors need from a level: committed block reads and writes (from <see cref="IBlockChangeWorld"/>), the game and day
/// clocks, sky light at a loaded position (0 when unloaded, so a query never loads a chunk), persisted block ticks, and deferred
/// simulation work that runs on the level's simulation path.
/// </summary>
internal interface IRedstoneLevel : IBlockChangeWorld
{
    IRandomSource Random { get; }

    long GameTime { get; }

    long DayTime { get; }

    int GetSkyLight(Vector position);

    /// <summary>Schedules a persisted tick of <paramref name="block"/> at <paramref name="position"/>, deduplicated by block type.</summary>
    void ScheduleBlockTick(Vector position, IBlock block, int delay, Obsidian.API.Blocks.TickPriority priority = Obsidian.API.Blocks.TickPriority.Normal);

    /// <summary>Whether a tick of <paramref name="block"/> is already pending at <paramref name="position"/>.</summary>
    bool IsBlockTickScheduled(Vector position, string block);

    /// <summary>Queues work for the level's simulation path, to run after the current block-tick stage.</summary>
    void EnqueueSimulation(Func<ValueTask> action);

    /// <summary>
    /// Commits a block state. <paramref name="notifyNeighbors"/> false is vanilla's flag 2, which updates clients without
    /// notifying neighbors.
    /// </summary>
    ValueTask WriteAsync(Vector position, IBlock block, bool notifyNeighbors);

    /// <summary>Commits a component's state with its exact vanilla update flags.</summary>
    ValueTask WriteAsync(Vector position, IBlock block, BlockUpdateFlags flags);

    /// <summary>Refreshes the six neighbours' shapes after a write that deferred shape updates.</summary>
    ValueTask UpdateNeighborShapesAsync(Vector position);

    /// <summary>Restores a block internally, without client, neighbor, shape or placement notifications.</summary>
    ValueTask RestoreBlockSilentlyAsync(Vector position, IBlock block);

    /// <summary>Whether the <c>tnt_explodes</c> rule lets TNT be primed.</summary>
    bool TntExplodes { get; }

    /// <summary>Spawns lit TNT at <paramref name="position"/> on the level's entity path, after the block has been removed.</summary>
    void PrimeTnt(Vector position);

    /// <summary>Vanilla <c>levelEvent</c>: a sound or particles players near <paramref name="position"/> see.</summary>
    void BroadcastLevelEvent(int type, Vector position, int data);

    /// <summary>
    /// Vanilla <c>gameEvent</c> at the center of the block at <paramref name="position"/>, which sculk sensors and
    /// wardens hear. Worlds without listeners ignore it.
    /// </summary>
    void EmitGameEvent(MobGameEvent kind, Vector position, IEntity? source)
    {
    }

    /// <summary>Queues a validated, coalesced event for the shared block-event phase.</summary>
    void QueueBlockEvent(Vector position, int blockId, int type, int data, Func<ValueTask<bool>> trigger);

    /// <summary>Destroys the block at <paramref name="position"/> with its drops, now, so the caller can overwrite the cell (vanilla <c>dropResources</c> and <c>removeBlock</c>).</summary>
    ValueTask DestroyBlockNowAsync(Vector position, IBlock block, bool playEffect);

    /// <summary>Notifies the block at <paramref name="target"/> that <paramref name="source"/> changed (vanilla <c>neighborChanged</c>), on the notification queue.</summary>
    ValueTask NotifyNeighborAsync(Vector target, Vector source);

    /// <summary>Vanilla <c>updateNeighbourForOutputSignal</c>: the comparators reading the block at <paramref name="position"/> hear of it.</summary>
    ValueTask NotifyComparatorsAroundAsync(Vector position);

    /// <summary>The block entity at <paramref name="position"/>, or null when there's none or its chunk isn't loaded.</summary>
    IBlockEntity? GetBlockEntityAt(Vector position);

    /// <summary>
    /// Makes <paramref name="entity"/> the block entity at <paramref name="position"/> (vanilla <c>setBlockEntity</c>). The block
    /// there must already be set; a block entity it replaces is removed.
    /// </summary>
    void SetBlockEntityAt(Vector position, IBlockEntity entity);

    /// <summary>The entities whose boxes overlap the box from <paramref name="min"/> to <paramref name="max"/>.</summary>
    IEnumerable<Obsidian.API.IEntity> EntitiesIn(VectorD min, VectorD max);

    /// <summary>Sets a crafter's triggered flag on its block entity (vanilla <c>CrafterBlockEntity.setTriggered</c>).</summary>
    void SetCrafterTriggered(Vector position, bool triggered);

    /// <summary>Fires a dispenser or dropper on the entity path (vanilla <c>DispenserBlock.dispenseFrom</c>).</summary>
    void FireDispenser(Vector position, IBlock state, bool dropper);

    /// <summary>Crafts a crafter's grid on the entity path (vanilla <c>CrafterBlock.dispenseFrom</c>).</summary>
    void CraftCrafter(Vector position);

    /// <summary>
    /// Runs a due tick of the command block at <paramref name="position"/> on the entity path (vanilla
    /// <c>CommandBlock.tick</c>), where its command can run. Worlds without commands do nothing.
    /// </summary>
    void RunCommandBlock(Vector position)
    {
    }

    /// <summary>
    /// How many viewers have the trapped chest at <paramref name="position"/> open, as its last opener recheck
    /// counted them (vanilla <c>ChestBlockEntity.getOpenCount</c>). Worlds without players have none.
    /// </summary>
    int TrappedChestViewers(Vector position) => 0;

    bool HasAnalogOutput(IBlock block);

    /// <summary>Vanilla's <c>getAnalogOutputSignal</c> of the block at <paramref name="position"/>, read as the neighbour in <paramref name="direction"/>.</summary>
    int GetAnalogOutput(Vector position, IBlock block, BlockFace direction);
}
