using Microsoft.Extensions.Logging;
using System.Diagnostics;
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

    // The share of the tick's 50 ms the level's own work may have taken for it to still save eagerly.
    private static readonly TimeSpan EagerSaveTickBudget = TimeSpan.FromMilliseconds(40);

    private int eagerScanCursor;
    private int eagerWrites;

    /// <summary>
    /// Saves some of the chunks that changed, at the end of the level's tick and under its simulation gate, waiting
    /// only until their snapshots are taken (see <see cref="Region.TrySaveChunks"/>): writing them goes on while the
    /// level ticks. Their entities are left to the autosave and unloads, as vanilla's are.
    /// </summary>
    /// <param name="tickStart">When the level's tick started (a <see cref="Stopwatch"/> timestamp).</param>
    private async Task SaveChunksEagerlyAsync(long tickStart)
    {
        if (Stopwatch.GetElapsedTime(tickStart) >= EagerSaveTickBudget)
            return;

        var capacity = MaxEagerWrites - Volatile.Read(ref this.eagerWrites);
        if (capacity <= 0)
            return;

        var due = this.TakeEagerSaves(Math.Min(EagerSavesPerTick, capacity));
        if (due.Count == 0)
            return;

        var snapshots = new List<Task>(due.Count);
        foreach (var (region, chunks) in due)
        {
            var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (region.TrySaveChunks(chunks, taken) is not Task written)
            {
                // Another save of the region is under way; the chunks are looked at again on a later pass.
                foreach (var chunk in chunks)
                    ((Chunk)chunk).NextEagerSave = 0;

                continue;
            }

            Interlocked.Add(ref this.eagerWrites, chunks.Count);
            snapshots.Add(taken.Task);
            _ = this.EndEagerSaveAsync(written, chunks.Count);
        }

        await Task.WhenAll(snapshots);
    }

    // Up to `limit` changed chunks to save this tick, by region, from the scan's window, each starting its cooldown.
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

            chunk.NextEagerSave = now + EagerSaveCooldownMilliseconds;
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
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save changed chunks of {LevelName} ahead of the autosave")]
        public static partial void EagerSaveFailed(ILogger logger, Exception exception, string levelName);
    }
}
