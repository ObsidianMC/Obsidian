using Microsoft.Extensions.Logging;
using System.Threading;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's ChunkMap.saveChunksEagerly: up to 20 changed chunks a tick, while fewer than 128 chunk writes are under
    // way and the tick has time left, each chunk at most once every 10 seconds. Keeping changed chunks written as they
    // change leaves the autosave little to take, which otherwise snapshots every chunk changed since the last one
    // while the level waits.
    private const int EagerSavesPerTick = 20;
    private const int MaxEagerWrites = 128;
    private const long EagerSaveCooldownMilliseconds = 10_000;

    // How many of the level's chunks a tick looks over for changes, from where the last tick stopped: vanilla queues a
    // chunk as it changes, which Obsidian's many ways of changing a chunk don't announce.
    private const int EagerScanPerTick = 256;

    /// <summary>
    /// The share of the tick's 50 ms the level's own work may have taken for it to still save eagerly. Tests lift it,
    /// so that a slow first tick (one still compiling, say) doesn't skip the save they check.
    /// </summary>
    internal TimeSpan EagerSaveTickBudget { get; set; } = TimeSpan.FromMilliseconds(40);

    private int eagerScanCursor;
    private int eagerWrites;

    // The eager saves still writing, which an autosave waits for before it takes the simulation gate.
    private readonly HashSet<Task> eagerSaves = [];

    // Autosaves waiting for eager saves to end; no eager save starts meanwhile, so the wait ends.
    private int autosavesWaiting;

    /// <summary>
    /// Saves some of the chunks that changed, at the end of the level's tick and under its simulation gate, waiting
    /// only until their snapshots are taken (see <see cref="Region.TrySaveChunks"/>): writing them goes on while the
    /// level ticks. Their entities are left to the autosave and unloads, as vanilla's are, once the region has
    /// written them.
    /// </summary>
    /// <param name="tickTime">How long the level's tick has taken so far.</param>
    private async Task SaveChunksEagerlyAsync(TimeSpan tickTime)
    {
        if (tickTime >= this.EagerSaveTickBudget || Volatile.Read(ref this.autosavesWaiting) != 0)
            return;

        var capacity = MaxEagerWrites - Volatile.Read(ref this.eagerWrites);
        if (capacity <= 0)
            return;

        var due = this.TakeEagerSaves(Math.Min(EagerSavesPerTick, capacity));
        if (due.Count == 0)
            return;

        var started = new List<(List<IChunk> Snapshotted, Task Snapshots)>(due.Count);
        foreach (var (region, chunks) in due)
        {
            var snapshotted = new List<IChunk>();
            var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Another save of the region is under way when it doesn't start; the chunks are looked at again on a later
            // pass.
            if (region.TrySaveChunks(chunks, snapshotted, taken) is not Task written)
                continue;

            Interlocked.Add(ref this.eagerWrites, chunks.Count);
            lock (this.eagerSaves)
                this.eagerSaves.Add(written);

            _ = this.EndEagerSaveAsync(written, chunks.Count);
            started.Add((snapshotted, taken.Task));
        }

        await Task.WhenAll(started.Select(save => save.Snapshots));

        // Only the chunks taken start their cooldown, here on the tick, the only one to read it: the region left the
        // others out, which a later pass looks at again.
        var next = Environment.TickCount64 + EagerSaveCooldownMilliseconds;
        foreach (var (snapshotted, _) in started)
        {
            foreach (var chunk in snapshotted.OfType<Chunk>())
                chunk.NextEagerSave = next;
        }
    }

    // Up to `limit` changed chunks whose cooldown is over, by region, from the scan's window.
    private Dictionary<Region, List<IChunk>> TakeEagerSaves(int limit)
    {
        var due = new Dictionary<Region, List<IChunk>>();
        var chunks = this.GeneratedChunksThisTick();
        if (chunks.Count == 0)
            return due;

        var now = Environment.TickCount64;
        var start = this.eagerScanCursor % chunks.Count;
        var window = Math.Min(EagerScanPerTick, chunks.Count);
        var picked = 0;
        var scanned = 0;
        while (scanned < window && picked < limit)
        {
            var chunk = chunks[(start + scanned) % chunks.Count];
            scanned++;

            if (now < chunk.NextEagerSave || !chunk.IsUnsaved)
                continue;

            if (this.GetRegionForChunk(chunk.X, chunk.Z) is not Region region)
                continue;

            if (!due.TryGetValue(region, out var batch))
                due[region] = batch = [];

            batch.Add(chunk);
            picked++;
        }

        this.eagerScanCursor = start + scanned;
        return due;
    }

    // A failed write puts the chunks' unsaved marks back (see Region.CompleteSaveAsync), so a later save retries them.
    private async Task EndEagerSaveAsync(Task written, int count)
    {
        try
        {
            await written;
        }
        catch (Exception exception)
        {
            Log.EagerSaveFailed(this.Logger, exception, this.DimensionName);
        }
        finally
        {
            Interlocked.Add(ref this.eagerWrites, -count);
            lock (this.eagerSaves)
                this.eagerSaves.Remove(written);
        }
    }

    /// <summary>
    /// Waits for the eager saves writing now (see <see cref="SaveChunksEagerlyAsync"/>) to end, whether or not they
    /// fail.
    /// </summary>
    internal async Task EagerSavesWrittenAsync()
    {
        Task[] writing;
        lock (this.eagerSaves)
            writing = [.. this.eagerSaves];

        // Their failures are logged (see EndEagerSaveAsync).
        await Task.WhenAll(writing).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>
    /// Takes the simulation gate for an autosave once no eager save is writing. An eager save holds its region's flush
    /// lock until its writes are committed, which the autosave's save of the region waits for: under the gate, the
    /// level couldn't tick meanwhile. Eager saves only start under the gate, and not while this waits.
    /// </summary>
    private async Task EnterGateAfterEagerSavesAsync()
    {
        Interlocked.Increment(ref this.autosavesWaiting);
        try
        {
            while (true)
            {
                await this.EagerSavesWrittenAsync();
                await this.simulationGate.WaitAsync();

                // One started by a tick that was past its check of autosavesWaiting.
                bool writing;
                lock (this.eagerSaves)
                    writing = this.eagerSaves.Count != 0;

                if (!writing)
                    return;

                this.simulationGate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref this.autosavesWaiting);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save changed chunks of {LevelName} ahead of the autosave")]
        public static partial void EagerSaveFailed(ILogger logger, Exception exception, string levelName);
    }
}
