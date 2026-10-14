using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// The level's tickers (see <see cref="AddTicker"/>), its block entities' load and chunk unload notices (see
/// <see cref="BlockEntity.OnLoad"/> and <see cref="BlockEntity.OnChunkUnloaded"/>), and the block and block entity
/// lookups that never load a chunk (see <see cref="GetLoadedBlock"/> and <see cref="GetLoadedBlockEntity"/>).
/// </summary>
public abstract partial class AbstractLevel
{
    // Copied on write under tickerLock, so the tick runs a snapshot without holding a lock over plugin code.
    private readonly Lock tickerLock = new();
    private LevelTicker[] tickers = [];

    // Plugin block entities placed in the level whose OnLoad runs on the level's next block entity tick.
    private readonly ConcurrentQueue<BlockEntity> pendingLoads = new();

    /// <inheritdoc/>
    public IDisposable AddTicker(Func<ValueTask> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        var owner = PluginEntities.FindSpawner();
        var ticker = new LevelTicker(this, owner, tick);

        // Under the lock ReleaseTickers takes after closing the plugin, so that no ticker outlives its plugin.
        lock (this.tickerLock)
        {
            owner?.ThrowIfClosing();
            this.tickers = [.. this.tickers, ticker];
        }

        return ticker;
    }

    /// <summary>
    /// Removes every ticker of <paramref name="plugin"/>, as it unloads, including those not disposed, and forgets its
    /// block entities still waiting for <see cref="BlockEntity.OnLoad"/>, so the level keeps none of its code alive.
    /// </summary>
    internal void ReleaseLifecycle(PluginContainer plugin)
    {
        LevelTicker[] released;
        lock (this.tickerLock)
        {
            released = [.. this.tickers.Where(ticker => ticker.Owner == plugin)];
            this.tickers = [.. this.tickers.Where(ticker => ticker.Owner != plugin)];
        }

        foreach (var ticker in released)
            ticker.Dispose();

        // Others queued meanwhile only change places in the queue.
        var count = this.pendingLoads.Count;
        for (var index = 0; index < count && this.pendingLoads.TryDequeue(out var blockEntity); index++)
        {
            if (blockEntity.Owner != plugin)
                this.pendingLoads.Enqueue(blockEntity);
        }
    }

    /// <inheritdoc/>
    public IBlock? GetLoadedBlock(Vector position)
    {
        var chunk = this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord());
        if (chunk is null || !chunk.IsGenerated)
            return null;

        return this.IsOutsideBuildHeight(position.Y)
            ? BlocksRegistry.VoidAir
            : chunk.GetBlock(position.X, position.Y, position.Z);
    }

    /// <inheritdoc/>
    public IBlockEntity? GetLoadedBlockEntity(Vector position)
    {
        var chunk = this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord());
        if (chunk is null || !chunk.IsGenerated)
            return null;

        var blockEntity = chunk.GetBlockEntity(position.X, position.Y, position.Z);
        if (blockEntity is BlockEntity { IsRemoved: true })
            return null;

        // Like GetBlockEntityAsync: a server block entity of a chunk that doesn't tick may not be placed yet.
        this.AttachBlockEntity(blockEntity);

        return blockEntity;
    }

    /// <summary>
    /// Queues a plugin's block entity just placed in the level (see <see cref="AttachBlockEntity"/>) for its
    /// <see cref="BlockEntity.OnLoad"/>.
    /// </summary>
    private void QueueLoad(BlockEntity blockEntity)
    {
        if (blockEntity.Owner is null)
            return;

        blockEntity.LoadPending = true;
        this.pendingLoads.Enqueue(blockEntity);
    }

    /// <summary>
    /// Runs <see cref="BlockEntity.OnLoad"/> for the block entities placed since the last block entity tick, on the
    /// level's tick, whether or not their chunks tick. Those placed by these calls wait for the next tick.
    /// </summary>
    private void RunPendingLoads()
    {
        var count = this.pendingLoads.Count;
        for (var index = 0; index < count && this.pendingLoads.TryDequeue(out var blockEntity); index++)
            this.RunLoad(blockEntity);
    }

    /// <summary>
    /// Runs a block entity's pending <see cref="BlockEntity.OnLoad"/>, if it's still live: before its first tick too,
    /// for one placed during the tick.
    /// </summary>
    private void RunLoad(BlockEntity blockEntity)
    {
        if (!blockEntity.LoadPending)
            return;

        blockEntity.LoadPending = false;
        if (!blockEntity.IsRemoved
            && ReferenceEquals(this.GetResidentBlockEntity(blockEntity.BlockPosition), blockEntity))
            this.CallPlugin(blockEntity, blockEntity.Owner as PluginContainer, static loaded => loaded.OnLoad());
    }

    /// <summary>
    /// Tells a block entity retired by its chunk's final save (see <see cref="BlockEntityLifecycle"/>) that its chunk
    /// unloaded, between the level's ticks and holding no lock. One whose <see cref="BlockEntity.OnLoad"/> never ran
    /// isn't told either.
    /// </summary>
    internal void OnBlockEntityUnloaded(BlockEntity blockEntity)
    {
        if (blockEntity.LoadPending)
        {
            blockEntity.LoadPending = false;
            return;
        }

        var plugin = blockEntity.Owner as PluginContainer;
        this.CallPlugin(blockEntity, plugin, static unloaded => unloaded.OnChunkUnloaded());
    }

    /// <summary>
    /// Runs the level's tickers in the order they were added, after its block entities ticked and before their changes
    /// are taken in and their updates sent: each inside a call into its plugin, its failures logged.
    /// </summary>
    private async ValueTask RunTickersAsync()
    {
        foreach (var ticker in Volatile.Read(ref this.tickers))
        {
            if (ticker.Tick is not Func<ValueTask> tick)
                continue;

            using var call = ticker.Owner?.TryEnter();
            if (ticker.Owner is not null && call is null)
                continue;

            try
            {
                await tick();
            }
            catch (Exception ex)
            {
                Log.TickerFailed(this.Logger, ex, ticker.Owner?.Info.Name ?? "the server", this.Name);
            }
        }
    }

    private void RemoveTicker(LevelTicker ticker)
    {
        lock (this.tickerLock)
            this.tickers = [.. this.tickers.Where(added => added != ticker)];
    }

    /// <summary>
    /// A ticker's handle. Disposing it drops the plugin's delegate, so that a handle kept elsewhere doesn't keep the
    /// plugin's code alive.
    /// </summary>
    private sealed class LevelTicker(AbstractLevel level, PluginContainer? owner, Func<ValueTask> tick) : IDisposable
    {
        private AbstractLevel? level = level;
        private Func<ValueTask>? tick = tick;

        public PluginContainer? Owner { get; } = owner;

        /// <summary>The ticker's code, or null once it's disposed.</summary>
        public Func<ValueTask>? Tick => Volatile.Read(ref this.tick);

        public void Dispose()
        {
            Volatile.Write(ref this.tick, null);
            Interlocked.Exchange(ref this.level, null)?.RemoveTicker(this);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "A level ticker of {Owner} failed in {Level}")]
        public static partial void TickerFailed(ILogger logger, Exception exception, string owner, string level);
    }
}
