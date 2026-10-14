using Obsidian.API.Blocks;
using Obsidian.WorldData.Ticks;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// A live level's scheduled ticks and block updates: the scheduled block and fluid ticks of its complete chunks, run
/// each level tick, and the neighbor and shape updates of block changes (see <see cref="FluidLevel"/>), with vanilla's
/// fluid behavior and the reactions of <see cref="Blocks"/>.
/// </summary>
/// <remarks>
/// Like vanilla, a chunk's ticks only run while the chunk and its eight neighbors are loaded and complete, so ticks
/// never write into chunks that are still generating. Everything touching ticks or the blocks they change goes through
/// one lock: the level's tick, block changes with updates, and the post-processing of generating chunks next to
/// complete ones.
/// </remarks>
internal sealed class LevelFluids : IFluidLevelAccess
{
    // How often chunks that were unloaded stop being tracked, in level ticks.
    private const int PruneInterval = 100;

    private readonly AbstractLevel level;
    private readonly TickScheduler<FluidKind> scheduler;
    private readonly Dictionary<long, Chunk> chunks = [];
    private readonly Lock sync = new();
    private readonly FluidLevel fluidLevel;

    public LevelFluids(AbstractLevel level)
    {
        this.level = level;
        this.scheduler = new TickScheduler<FluidKind>(this.Clock);
        this.fluidLevel = new FluidLevel(this);
        this.Blocks = new LevelBlockUpdates(level, this, this.fluidLevel);
    }

    /// <summary>
    /// The level's block ticks and the reactions of plugin and falling blocks to block updates.
    /// </summary>
    public LevelBlockUpdates Blocks { get; }

    /// <summary>
    /// The game time block and fluid ticks are scheduled against, like vanilla's level game time.
    /// </summary>
    public TickClock Clock { get; } = new();

    /// <inheritdoc/>
    public IBlockReactions? Reactions => this.Blocks;

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

    internal VectorD GetFlow(Vector position)
    {
        lock (sync)
        {
            var fluid = fluidLevel.GetFluid(position);
            return fluid.Type?.GetFlow(fluidLevel, position, fluid) ?? VectorD.Zero;
        }
    }

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
            {
                previous.FluidTicks.StopTicking();
                previous.BlockTicks.StopTicking();
            }

            this.chunks[key] = complete;
            complete.FluidTicks.StartTicking(this.scheduler, key);
            complete.BlockTicks.StartTicking(this.Blocks.Scheduler, key);
        }
    }

    /// <summary>
    /// Runs one level tick of block ticks and then fluid ticks, like vanilla.
    /// </summary>
    /// <remarks>
    /// The game time advances before either runs, as vanilla's does, so that a fluid tick a block tick schedules is
    /// due as many ticks later as one scheduled anywhere else.
    /// </remarks>
    public void Tick()
    {
        lock (this.sync)
        {
            this.Clock.Advance();
            this.Blocks.Scheduler.Tick(this.IsTicking, this.Blocks.RunScheduledTick);
            this.scheduler.Tick(this.IsTicking, this.fluidLevel.RunScheduledTick);
            if (this.Clock.GameTime % PruneInterval == 0)
                this.PruneUnloaded();
        }
    }

    /// <summary>
    /// Lets fluids react to a block a feature growing at runtime changed (see
    /// <see cref="FluidLevel.UpdateFluidsAround"/>).
    /// </summary>
    public void OnBlockChanged(Vector position, IBlock block)
    {
        lock (this.sync)
            this.fluidLevel.UpdateFluidsAround(position, block);
    }

    /// <summary>
    /// Runs the side effects of a block the level stored outside of ticks, from <paramref name="old"/>, as
    /// <paramref name="flags"/> ask (see <see cref="FluidLevel.OnBlockChanged"/>).
    /// </summary>
    public void OnBlockChanged(Vector position, IBlock old, IBlock block, BlockUpdateFlags flags)
    {
        lock (this.sync)
            this.fluidLevel.OnBlockChanged(position, old, block, flags);
    }

    /// <summary>
    /// The complete chunk holding <paramref name="position"/>, if it's loaded. Call it under <see cref="TickLock"/>,
    /// which tracking chunks takes.
    /// </summary>
    public bool TryGetChunk(Vector position, [NotNullWhen(true)] out Chunk? chunk) =>
        this.chunks.TryGetValue(TickScheduler<FluidKind>.ChunkKey(position), out chunk);

    /// <summary>
    /// The lock fluid ticks and fluid block changes run under, which random ticks and bone meal grow plants under and
    /// the level's block writes (<see cref="AbstractLevel.SetBlockUntrackedAsync(int, int, int, IBlock, bool)"/>) take,
    /// so that none of them writes over a change made between its read and its write. Holding it keeps them from
    /// running, for code that reads a complete chunk's blocks and ticks together (saving it). Take it after
    /// generation's chunk locks, never before, and don't wait on anything while holding it.
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
        !this.level.IsOutsideBuildHeight(position.Y) && this.TryGetChunk(position, out var chunk)
            ? chunk.GetBlock(position.X, position.Y, position.Z)
            : BlocksRegistry.VoidAir;

    /// <inheritdoc/>
    public bool SetBlock(Vector position, IBlock block)
    {
        if (this.level.IsOutsideBuildHeight(position.Y) || !this.TryGetChunk(position, out var chunk))
            return false;

        chunk.SetBlock(position.X, position.Y, position.Z, block);

        // Generated block entity data doesn't carry over to another block, like AbstractLevel.SetBlockUntrackedAsync.
        if (chunk.GetBlockEntity(position.X, position.Y, position.Z) is DataBlockEntity data && data.Id != block.BlockEntityType())
            chunk.RemoveBlockEntity(position.X, position.Y, position.Z);

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Players see the change with <see cref="BlockUpdateFlags.Clients"/>, and portals check their frames around it
    /// with <see cref="BlockUpdateFlags.Neighbors"/>.
    /// </remarks>
    public void OnBlockStored(Vector position, IBlock block, BlockUpdateFlags flags)
    {
        if ((flags & BlockUpdateFlags.Clients) != 0)
            this.level.BroadcastBlockChange(block, position);

        if ((flags & BlockUpdateFlags.Neighbors) != 0)
            this.level.Portals.OnBlockChanged(position);
    }

    /// <inheritdoc/>
    public void LevelEvent(int type, Vector position, int data) => this.level.BroadcastLevelEvent(type, position, data);

    /// <inheritdoc/>
    /// <remarks>
    /// Like vanilla, ticks for chunks that aren't loaded are dropped.
    /// </remarks>
    public void ScheduleFluidTick(Vector position, FluidKind fluid, int delay)
    {
        if (this.TryGetChunk(position, out var chunk))
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
            chunk.BlockTicks.StopTicking();
            this.chunks.Remove(key);
        }
    }
}
