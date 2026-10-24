using System.Threading;

namespace Obsidian.WorldData.BlockTicks;

/// <summary>
/// The read access block ticks need from a level. Queries never load or generate chunks.
/// </summary>
internal interface IBlockTickLevel
{
    /// <summary>
    /// The block at <paramref name="position"/>, or <c>null</c> when its chunk isn't loaded.
    /// </summary>
    IBlock? GetLoadedBlock(Vector position);

    /// <summary>
    /// Whether the chunk with <paramref name="key"/> is loaded.
    /// </summary>
    bool IsChunkLoaded(long key);
}

/// <summary>
/// A live level's scheduled block ticks: the ticks of its complete chunks, run once per level tick before fluid ticks.
/// </summary>
/// <remarks>
/// Like vanilla, a chunk's ticks only run while the chunk and its eight neighbors are loaded and complete. A due tick is
/// dropped when its block is no longer the block it was scheduled for, like vanilla's <c>ServerLevel.tickBlock</c>.
/// Everything touching block ticks runs under the lock the owner passes in, shared with fluid ticks.
/// </remarks>
internal sealed class LevelBlockTicks
{
    // How often chunks that were unloaded stop being tracked, in level ticks.
    private const int PruneInterval = 100;

    private readonly IBlockTickLevel level;
    private readonly BlockTickScheduler scheduler = new();
    private readonly Dictionary<long, Chunk> chunks = [];
    private readonly Dictionary<string, Action<Vector, IBlock>> behaviors = new(StringComparer.Ordinal);
    private readonly Lock sync;
    private readonly Action<Vector, IBlock>? unregistered;

    /// <param name="level">The level's block reads.</param>
    /// <param name="sync">The lock ticks and their block changes run under (the level's shared tick lock).</param>
    /// <param name="unregistered">Runs a due tick of a block with no registration, while its block is still there.</param>
    public LevelBlockTicks(IBlockTickLevel level, Lock sync, Action<Vector, IBlock>? unregistered = null)
    {
        this.level = level;
        this.sync = sync;
        this.unregistered = unregistered;
    }

    /// <summary>
    /// The game time the block ticks run on.
    /// </summary>
    public long GameTime
    {
        get
        {
            lock (this.sync)
                return this.scheduler.GameTime;
        }
    }

    /// <summary>
    /// The number of tracked chunks (complete and ticking, or waiting for their neighbors).
    /// </summary>
    public int TrackedChunks
    {
        get
        {
            lock (this.sync)
                return this.chunks.Count;
        }
    }

    /// <summary>
    /// Registers what runs when a tick of <paramref name="block"/> (its unlocalized name) comes due and its block is still
    /// there. Blocks without a registration have no scheduled behavior yet; their due ticks are consumed.
    /// </summary>
    public void Register(int blockId, Action<Vector, IBlock> behavior) => this.Register(BlocksRegistry.GetDefault(blockId).UnlocalizedName, behavior);

    public void Register(string block, Action<Vector, IBlock> behavior)
    {
        lock (this.sync)
            this.behaviors[block] = behavior;
    }

    /// <summary>
    /// Starts ticking a complete chunk's block ticks. Chunks that aren't complete, or already tick, are ignored.
    /// </summary>
    public void Track(IChunk chunk)
    {
        if (chunk is not Chunk complete || !complete.IsGenerated || complete.BlockTicks.IsTicking)
            return;

        lock (this.sync)
        {
            var key = NumericsHelper.IntsToLong(complete.X, complete.Z);
            if (this.chunks.TryGetValue(key, out var previous) && previous != complete)
                previous.BlockTicks.StopTicking();

            this.chunks[key] = complete;
            complete.BlockTicks.StartTicking(this.scheduler, key);
        }
    }

    /// <summary>
    /// Stops ticking an unloaded chunk's block ticks, keeping them with the chunk.
    /// </summary>
    public void Untrack(IChunk chunk)
    {
        lock (this.sync)
        {
            var key = NumericsHelper.IntsToLong(chunk.X, chunk.Z);
            if (!this.chunks.TryGetValue(key, out var stored) || !ReferenceEquals(stored, chunk))
                return;

            stored.BlockTicks.StopTicking();
            this.chunks.Remove(key);
        }
    }

    /// <summary>
    /// Whether a due tick of <paramref name="block"/> at <paramref name="position"/> is still to run this game tick.
    /// </summary>
    public bool WillTickThisTick(Vector position, string block)
    {
        lock (this.sync)
            return this.scheduler.WillTickThisTick(position, block);
    }

    /// <summary>
    /// Schedules a block tick at <paramref name="position"/>. Like vanilla, ticks for chunks that aren't loaded are dropped.
    /// </summary>
    public void Schedule(Vector position, string block, int delay, int priority = TickPriority.Normal)
    {
        lock (this.sync)
        {
            if (this.chunks.TryGetValue(BlockTickScheduler.ChunkKey(position), out var chunk))
                chunk.BlockTicks.Schedule(position, block, delay, priority);
        }
    }

    /// <summary>
    /// Whether a tick of <paramref name="block"/> is pending at <paramref name="position"/>. False for unloaded chunks.
    /// </summary>
    public bool IsScheduled(Vector position, string block)
    {
        lock (this.sync)
            return this.chunks.TryGetValue(BlockTickScheduler.ChunkKey(position), out var chunk) && chunk.BlockTicks.IsScheduled(position, block);
    }

    /// <summary>
    /// Runs one level tick of block ticks.
    /// </summary>
    public void Tick()
    {
        lock (this.sync)
        {
            this.scheduler.Tick(this.IsTicking, this.RunDueTick);
            if (this.scheduler.GameTime % PruneInterval == 0)
                this.PruneUnloaded();
        }
    }

    // Vanilla's tickBlock: the tick runs only if its block is still the block it was scheduled for.
    private void RunDueTick(ScheduledBlockTick tick)
    {
        var block = this.level.GetLoadedBlock(tick.Position);
        if (block is null || block.UnlocalizedName != tick.Block)
            return;

        if (this.behaviors.TryGetValue(tick.Block, out var behavior))
            behavior(tick.Position, block);
        else
            this.unregistered?.Invoke(tick.Position, block);
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
                if (!this.chunks.ContainsKey(neighbor) || !this.level.IsChunkLoaded(neighbor))
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
            if (this.level.IsChunkLoaded(key))
                continue;

            chunk.BlockTicks.StopTicking();
            this.chunks.Remove(key);
        }
    }
}
