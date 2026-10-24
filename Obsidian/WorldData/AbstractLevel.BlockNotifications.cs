using Obsidian.API.Blocks;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// The level as the block notification pipeline and the redstone engine see it. Reads and writes never load chunks: a
/// position in an unloaded chunk reads as unpowered, and a write there is dropped, like vanilla's <c>setBlock</c>.
/// </summary>
public abstract partial class AbstractLevel : IBlockChangeWorld, IPressurePlateWorld, IRedstoneLevel, IBlockTickLevel
{
    // Vanilla's flag 2 (clients only), as the redstone writes use it.
    private const BlockUpdateFlags ClientsOnly = BlockUpdateFlags.Clients | BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    /// <summary>The neighbor reactions of the redstone engine, keyed by block.</summary>
    internal NeighborBehaviors Neighbors { get; }

    /// <summary>The redstone signal behaviors, keyed by block class.</summary>
    internal RedstoneBehaviors RedstoneRules { get; }

    /// <summary>The queue that runs neighbor reactions after each committed block change.</summary>
    internal BlockNotificationPipeline BlockNotifications { get; }

    /// <summary>The installed redstone engine, with its tripwires and targets.</summary>
    internal RedstoneInstall RedstoneInstall { get; }

    /// <summary>The pressure plates, detected once per level tick.</summary>
    internal PressurePlates Plates { get; }

    /// <summary>The level's redstone engine (signal reads).</summary>
    internal RedstoneEngine RedstoneEngine => this.RedstoneInstall.Engine;

    int IBlockChangeWorld.MinY => this.MinY;

    int IBlockChangeWorld.Height => this.Height;

    IBlock? IBlockChangeWorld.GetLoadedBlock(Vector position)
    {
        if (this.IsOutsideBuildHeight(position.Y))
            return BlocksRegistry.VoidAir;

        return this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord())?.GetBlock(position.X, position.Y, position.Z);
    }

    int IBlockChangeWorld.GetRawBrightness(Vector position) => this.Fluids.GetRawBrightness(position);

    ValueTask IBlockChangeWorld.SetBlockAsync(Vector position, IBlock block) => this.WriteAsync(position, block, BlockUpdateFlags.All);

    void IBlockChangeWorld.DropAndRemove(Vector position, IBlock block, bool playEffect)
    {
        // Wire and rails are removed through dropResources/removeBlock, which emits no break effect.
        if (block.BlockClass() == "RedStoneWireBlock" || Rails.IsRail(block))
            playEffect = false;

        this.QueueBlockDestruction(position, true, null, replacement: null, playEffect,
            stillUnsupported: current => current.BlockClass() is "RedstoneTorchBlock"
                ? this.BlockUpdates.GetBlock(position + Vector.Down).IsTopCenterSturdy() == false
                : this.BlockUpdates.IsUnsupported(position, current));
    }

    void IBlockChangeWorld.ScheduleFluidTick(Vector position) => this.Fluids.ScheduleShapeFluidTick(position);

    async ValueTask IBlockChangeWorld.NotifyNeighborsAsync(Vector position)
    {
        this.BlockNotifications.EnqueueNeighbors(position);
        await this.BlockNotifications.DrainAsync();
    }

    void IPressurePlateWorld.SchedulePlateTick(Vector position, IBlock block, int delay) => this.ScheduleBlockTick(position, block, delay);

    long IRedstoneLevel.GameTime => this.Fluids.Clock.GameTime;

    long IRedstoneLevel.DayTime => this.LevelData.DayTime;

    int IRedstoneLevel.GetSkyLight(Vector position) =>
        this.IsOutsideBuildHeight(position.Y) ? 0 :
        this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord())?.GetLightLevel(position.X, position.Y, position.Z, LightType.Sky) ?? 0;

    bool IRedstoneLevel.HasAnalogOutput(IBlock block)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.HasAnalogOutput(block);
    }

    int IRedstoneLevel.GetAnalogOutput(Vector position, IBlock block, BlockFace direction)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.AnalogOutputOf(block, position, direction);
    }

    void IRedstoneLevel.ScheduleBlockTick(Vector position, IBlock block, int delay, Obsidian.API.Blocks.TickPriority priority) =>
        this.ScheduleBlockTick(position, block, delay, priority);

    bool IRedstoneLevel.IsBlockTickScheduled(Vector position, string block)
    {
        lock (this.Fluids.TickLock)
            return this.BlockUpdates.Ticks.IsScheduled(position, block);
    }

    void IRedstoneLevel.EnqueueSimulation(Func<ValueTask> action) => this.EnqueueSimulationAction(action);

    bool IRedstoneLevel.TntExplodes => this.LevelData.GetBooleanRule("tnt_explodes");

    void IRedstoneLevel.SetCrafterTriggered(Vector position, bool triggered)
    {
        if (this.BlockUpdates.GetBlockEntity(position) is CrafterBlockEntity crafter)
        {
            crafter.Crafter.Triggered = triggered;
            crafter.SetChanged();
        }
    }

    void IRedstoneLevel.FireDispenser(Vector position, IBlock state, bool dropper) =>
        this.EnqueueEntityAction(() => RedstoneDispenser.Fire(this, position, state, dropper));

    void IRedstoneLevel.CraftCrafter(Vector position) =>
        this.EnqueueEntityAction(() => RedstoneCrafter.CraftAsync(this, position));

    void IRedstoneLevel.PrimeTnt(Vector position) =>
        this.EnqueueEntityAction(() =>
        {
            Entities.PrimedTnt.Prime(this, position, owner: null);
            return ValueTask.CompletedTask;
        });

    ValueTask IRedstoneLevel.WriteAsync(Vector position, IBlock block, bool notifyNeighbors) =>
        this.WriteAsync(position, block, notifyNeighbors ? BlockUpdateFlags.All : ClientsOnly);

    IBlock? IBlockTickLevel.GetLoadedBlock(Vector position) => ((IBlockChangeWorld)this).GetLoadedBlock(position);

    bool IBlockTickLevel.IsChunkLoaded(long key)
    {
        NumericsHelper.LongToInts(key, out var chunkX, out var chunkZ);
        return this.GetLoadedChunk(chunkX, chunkZ) is not null;
    }

    // A write into an unloaded chunk is dropped: the engine never loads chunks.
    private ValueTask WriteAsync(Vector position, IBlock block, BlockUpdateFlags flags)
    {
        if (this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord()) is null)
            return ValueTask.CompletedTask;

        return this.SetBlockUntrackedAsync(position.X, position.Y, position.Z, block, flags);
    }
}
