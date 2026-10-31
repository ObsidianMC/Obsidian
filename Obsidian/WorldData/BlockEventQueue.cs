namespace Obsidian.WorldData;

internal readonly record struct BlockEventKey(Vector Position, int Block, int Id, int Parameter);

/// <summary>The level's pending block events, independent of the locks used by block callbacks.</summary>
internal sealed class BlockEventQueue
{
    private readonly object sync = new();
    private readonly Queue<(BlockEventKey Key, Func<ValueTask> Run)> events = [];
    private readonly HashSet<BlockEventKey> pending = [];
    private bool draining;

    internal int Count
    {
        get
        {
            lock (this.sync)
            {
                return this.events.Count;
            }
        }
    }

    internal void Enqueue(BlockEventKey key, Func<ValueTask> run)
    {
        lock (this.sync)
        {
            if (this.pending.Add(key))
            {
                this.events.Enqueue((key, run));
            }
        }
    }

    internal async ValueTask DrainAsync(Func<Vector, bool> ticking, Func<Vector, int?> currentBlock,
        Action? beforeNextEvent = null)
    {
        lock (this.sync)
        {
            if (this.draining)
                return;

            this.draining = true;
        }

        var deferred = new List<(BlockEventKey Key, Func<ValueTask> Run)>();
        try
        {
            // Vanilla ServerLevel.runBlockEvents: insertion order, coalescing by position/block/id/parameter,
            // removing before dispatch so callbacks can queue another event in this same drain.
            while (true)
            {
                // Reconcile window lifecycle changes even when an earlier block callback caused them.
                // This hook, like the callbacks, runs outside the queue lock.
                beforeNextEvent?.Invoke();
                (BlockEventKey Key, Func<ValueTask> Run) entry;
                lock (this.sync)
                {
                    if (!this.events.TryDequeue(out entry))
                        break;

                    this.pending.Remove(entry.Key);
                }

                // No queue lock is held while consulting chunks, taking the tick lock, or awaiting a callback.
                if (!ticking(entry.Key.Position))
                {
                    deferred.Add(entry);
                    continue;
                }

                if (currentBlock(entry.Key.Position) == entry.Key.Block)
                {
                    await entry.Run();
                }
            }
        }
        finally
        {
            lock (this.sync)
            {
                // Inactive chunks retry next tick, after all currently runnable events, as in vanilla.
                foreach (var entry in deferred)
                {
                    if (this.pending.Add(entry.Key))
                    {
                        this.events.Enqueue(entry);
                    }
                }

                this.draining = false;
            }
        }
    }
}
