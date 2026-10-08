using System.Threading;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// A live level's fluids: the scheduled fluid ticks of its complete chunks, run each level tick with vanilla's fluid
/// behavior.
/// </summary>
/// <remarks>
/// Like vanilla, a chunk's ticks only run while the chunk and its eight neighbors are loaded and complete, so fluids never
/// write into chunks that are still generating. Everything touching fluid ticks or the blocks they change goes through one
/// lock: the level's tick, block changes with updates, and the post-processing of generating chunks next to complete ones.
/// </remarks>
internal sealed class LevelFluids : IFluidLevelAccess
{
    // How often chunks that were unloaded stop being tracked, in level ticks.
    private const int PruneInterval = 100;

    private readonly AbstractLevel level;
    private readonly FluidTickScheduler scheduler = new();
    private readonly Dictionary<long, Chunk> chunks = [];
    private readonly Lock sync = new();
    private readonly FluidLevel fluidLevel;

    public LevelFluids(AbstractLevel level)
    {
        this.level = level;
        this.fluidLevel = new FluidLevel(this);
    }

    /// <inheritdoc/>
    public int MinY => this.level.MinY;

    /// <inheritdoc/>
    public int Height => this.level.Height;

    /// <summary>
    /// The dimension setting and game rules fluids follow; vanilla's overworld defaults until the level sets its dimension.
    /// </summary>
    public FluidRules Rules { get; set; } = FluidRules.ForDimension("minecraft:overworld");

    /// <inheritdoc/>
    /// <remarks>
    /// Unseeded, like vanilla's level random.
    /// </remarks>
    public Random Random { get; } = new();

    /// <summary>
    /// Starts ticking a complete chunk's fluid ticks, like vanilla when a chunk starts ticking. Chunks that aren't complete,
    /// or already tick, are ignored.
    /// </summary>
    public void Track(IChunk chunk)
    {
        if (chunk is not Chunk complete || !complete.IsGenerated || complete.FluidTicks.IsTicking)
            return;

        lock (this.sync)
        {
            var key = NumericsHelper.IntsToLong(complete.X, complete.Z);
            if (this.chunks.TryGetValue(key, out var previous) && previous != complete)
                previous.FluidTicks.StopTicking();

            this.chunks[key] = complete;
            complete.FluidTicks.StartTicking(this.scheduler, key);
        }
    }

    /// <summary>
    /// Runs one level tick of fluid ticks.
    /// </summary>
    public void Tick()
    {
        lock (this.sync)
        {
            this.scheduler.Tick(this.IsTicking, this.fluidLevel.RunScheduledTick);
            if (this.scheduler.GameTime % PruneInterval == 0)
                this.PruneUnloaded();
        }
    }

    /// <summary>
    /// Lets fluids react to a block changed with block updates outside of fluid ticks (see <see cref="FluidLevel.OnBlockChanged"/>).
    /// </summary>
    public void OnBlockChanged(Vector position, IBlock block)
    {
        lock (this.sync)
            this.fluidLevel.OnBlockChanged(position, block);
    }

    /// <summary>
    /// The lock fluid ticks and fluid block changes run under. Holding it keeps them from running, for code that reads a
    /// complete chunk's blocks and ticks together (saving it). Take it after generation's chunk locks, never before.
    /// </summary>
    public Lock TickLock => this.sync;

    /// <summary>
    /// Runs <paramref name="action"/> while no fluid tick runs, for code that changes complete chunks' blocks or ticks
    /// from another thread (generation post-processing a chunk next to complete ones).
    /// </summary>
    public void RunExclusive(Action action)
    {
        lock (this.sync)
            action();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Chunks that aren't loaded read as void air too; fluid ticks only run where every chunk they can reach is loaded.
    /// </remarks>
    public IBlock GetBlock(Vector position) =>
        !this.level.IsOutsideBuildHeight(position.Y) && this.chunks.TryGetValue(FluidTickScheduler.ChunkKey(position), out var chunk)
            ? chunk.GetBlock(position.X, position.Y, position.Z)
            : BlocksRegistry.VoidAir;

    /// <inheritdoc/>
    public bool SetBlock(Vector position, IBlock block)
    {
        if (this.level.IsOutsideBuildHeight(position.Y) || !this.chunks.TryGetValue(FluidTickScheduler.ChunkKey(position), out var chunk))
            return false;

        chunk.SetBlock(position.X, position.Y, position.Z, block);

        // Generated block entity data doesn't carry over to another block, like AbstractLevel.SetBlockUntrackedAsync.
        if (chunk.GetBlockEntity(position.X, position.Y, position.Z) is DataBlockEntity data && data.Id != block.BlockEntityType())
            chunk.RemoveBlockEntity(position.X, position.Y, position.Z);

        this.level.BroadcastBlockChange(block, position);
        this.level.Portals.OnBlockChanged(position);
        return true;
    }

    /// <inheritdoc/>
    public void LevelEvent(int type, Vector position, int data) => this.level.BroadcastLevelEvent(type, position, data);

    /// <inheritdoc/>
    /// <remarks>
    /// Like vanilla, ticks for chunks that aren't loaded are dropped.
    /// </remarks>
    public void ScheduleFluidTick(Vector position, FluidKind fluid, int delay)
    {
        if (this.chunks.TryGetValue(FluidTickScheduler.ChunkKey(position), out var chunk))
            chunk.FluidTicks.Schedule(position, fluid, delay);
    }

    // Vanilla's tick check: the chunk ticks once it and its neighbors are loaded and complete.
    private bool IsTicking(long key)
    {
        NumericsHelper.LongToInts(key, out var chunkX, out var chunkZ);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var neighbor = NumericsHelper.IntsToLong(chunkX + dx, chunkZ + dz);
                if (!this.chunks.ContainsKey(neighbor) || !this.level.LoadedChunks.Contains(neighbor))
                    return false;
            }
        }

        return true;
    }

    // Unloaded chunks stop ticking; their ticks were saved with them (or stay with the chunk if it's still around).
    private void PruneUnloaded()
    {
        foreach (var (key, chunk) in this.chunks.ToArray())
        {
            if (this.level.LoadedChunks.Contains(key))
                continue;

            chunk.FluidTicks.StopTicking();
            this.chunks.Remove(key);
        }
    }
}
