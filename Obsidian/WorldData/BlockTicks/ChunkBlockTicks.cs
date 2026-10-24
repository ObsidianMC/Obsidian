using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData.BlockTicks;

/// <summary>
/// A chunk's scheduled block ticks: vanilla's <c>ProtoChunkTicks</c> while the chunk generates (ticks wait with a delay
/// relative to when the chunk starts ticking) and its <c>LevelChunkTicks</c> once a level ticks it (ticks are due at
/// absolute game ticks).
/// </summary>
/// <remarks>
/// A position holds at most one tick per block type; scheduling another is ignored, like vanilla. Thread-safe, since
/// generation, the level's tick and saving all reach it.
/// </remarks>
internal sealed class ChunkBlockTicks
{
    private readonly Lock sync = new();
    private readonly List<SavedBlockTick> pending = [];
    private readonly PriorityQueue<ScheduledBlockTick, ScheduledBlockTick> queue = new(ScheduledBlockTick.DrainOrder);
    private readonly HashSet<(Vector Position, string Block)> scheduled = [];
    private BlockTickScheduler? scheduler;
    private bool changed;

    /// <summary>
    /// The number of scheduled ticks.
    /// </summary>
    public int Count
    {
        get
        {
            lock (this.sync)
                return this.pending.Count + this.queue.Count;
        }
    }

    /// <summary>
    /// Whether the saved ticks differ from the chunk's last save: ticks were added or removed since, or a level ticks
    /// them, so the delays saved relative to now change every game tick.
    /// </summary>
    public bool IsUnsaved
    {
        get
        {
            lock (this.sync)
                return this.changed || this.scheduler is not null && this.queue.Count > 0;
        }
    }

    /// <summary>
    /// Clears the mark <see cref="IsUnsaved"/> reads, for a save about to write the ticks; returns what it was.
    /// </summary>
    public bool TakeUnsaved()
    {
        lock (this.sync)
        {
            var unsaved = this.changed || this.scheduler is not null && this.queue.Count > 0;
            this.changed = false;
            return unsaved;
        }
    }

    /// <summary>
    /// Whether a level ticks the chunk (its ticks are due at absolute game ticks).
    /// </summary>
    public bool IsTicking => Volatile.Read(ref this.scheduler) is not null;

    /// <summary>
    /// The chunk's key in the scheduler ticking it.
    /// </summary>
    internal long Key { get; private set; }

    /// <summary>
    /// Schedules a tick of <paramref name="block"/> at <paramref name="position"/> in <paramref name="delay"/> ticks, unless
    /// that block type already has one there.
    /// </summary>
    public void Schedule(Vector position, string block, int delay, int priority = TickPriority.Normal)
    {
        lock (this.sync)
        {
            if (!this.scheduled.Add((position, block)))
                return;

            this.changed = true;
            if (this.scheduler is null)
            {
                this.pending.Add(new SavedBlockTick(position, block, delay, priority));
                return;
            }

            var tick = new ScheduledBlockTick(position, block, this.scheduler.GameTime + delay, priority, this.scheduler.NextSubTickOrder());
            this.queue.Enqueue(tick, tick);
            this.scheduler.OnTickAdded(this, tick);
        }
    }

    /// <summary>
    /// Whether a tick of <paramref name="block"/> is pending at <paramref name="position"/> (vanilla <c>hasScheduledTick</c>).
    /// </summary>
    public bool IsScheduled(Vector position, string block)
    {
        lock (this.sync)
            return this.scheduled.Contains((position, block));
    }

    /// <summary>
    /// Whether a tick of <paramref name="block"/> is pending at <paramref name="position"/>; same as <see cref="IsScheduled"/>.
    /// </summary>
    public bool Contains(Vector position, string block) => this.IsScheduled(position, block);

    /// <summary>
    /// The ticks to save with the chunk, with delays relative to now (vanilla <c>pack</c>).
    /// </summary>
    /// <remarks>
    /// Waiting ticks come first, then due ones in the order they'd run (vanilla lists the latter in heap order).
    /// </remarks>
    public List<SavedBlockTick> Pack()
    {
        lock (this.sync)
        {
            var ticks = new List<SavedBlockTick>(this.pending);
            if (this.scheduler is not null)
            {
                var now = this.scheduler.GameTime;
                ticks.AddRange(this.queue.UnorderedItems.Select(entry => entry.Element).Order(ScheduledBlockTick.DrainOrder)
                    .Select(tick => new SavedBlockTick(tick.Position, tick.Block, (int)(tick.TriggerTick - now), tick.Priority)));
            }

            return ticks;
        }
    }

    /// <summary>
    /// Adds saved ticks before the chunk ticks, like vanilla loading a chunk's <c>block_ticks</c>.
    /// </summary>
    public void Load(IEnumerable<SavedBlockTick> ticks)
    {
        lock (this.sync)
        {
            foreach (var tick in ticks)
            {
                if (this.scheduled.Add((tick.Position, tick.Block)))
                {
                    this.pending.Add(tick);
                    this.changed = true;
                }
            }
        }
    }

    /// <summary>
    /// Reads vanilla's <c>block_ticks</c> list of chunk (<paramref name="chunkX"/>, <paramref name="chunkZ"/>): compounds
    /// with the block name (<c>i</c>), position (<c>x</c>, <c>y</c>, <c>z</c>), delay (<c>t</c>) and priority (<c>p</c>).
    /// Like vanilla, ticks outside the chunk and ticks without a block are dropped.
    /// </summary>
    public void Read(NbtList ticks, int chunkX, int chunkZ) => this.Load(ticks.OfType<NbtCompound>()
        .Select(tick => new SavedBlockTick(new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z")), tick.GetString("i") ?? string.Empty,
            tick.GetInt("t"), tick.GetInt("p")))
        .Where(tick => tick.Block.Length > 0 && tick.Position.X >> 4 == chunkX && tick.Position.Z >> 4 == chunkZ));

    /// <summary>
    /// Writes the ticks as vanilla's <c>block_ticks</c> list.
    /// </summary>
    public void Write(NbtWriterStream writer)
    {
        var ticks = this.Pack();
        writer.WriteListStart("block_ticks", NbtTagType.Compound, ticks.Count);
        foreach (var tick in ticks)
        {
            writer.WriteCompoundStart();
            writer.WriteString("i", tick.Block);
            writer.WriteInt("x", tick.Position.X);
            writer.WriteInt("y", tick.Position.Y);
            writer.WriteInt("z", tick.Position.Z);
            writer.WriteInt("t", tick.Delay);
            writer.WriteInt("p", tick.Priority);
            writer.EndCompound();
        }

        writer.EndList();
    }

    /// <summary>
    /// Hands the ticks to <paramref name="ticker"/>, which runs them from now on (vanilla registering the container and
    /// unpacking it when the chunk starts ticking).
    /// </summary>
    /// <remarks>
    /// Saved ticks get negative scheduling orders, so they run before ticks scheduled the same game tick, like vanilla.
    /// </remarks>
    internal void StartTicking(BlockTickScheduler ticker, long key)
    {
        lock (this.sync)
        {
            if (this.scheduler is not null)
                return;

            long order = -this.pending.Count;
            foreach (var saved in this.pending)
            {
                var tick = new ScheduledBlockTick(saved.Position, saved.Block, ticker.GameTime + saved.Delay, saved.Priority, order++);
                this.queue.Enqueue(tick, tick);
            }

            this.pending.Clear();
            this.changed = true;
            this.Key = key;
            Volatile.Write(ref this.scheduler, ticker);
            ticker.Register(this);
        }
    }

    /// <summary>
    /// Takes the ticks back from the scheduler, keeping them as saved ticks relative to now.
    /// </summary>
    internal void StopTicking()
    {
        lock (this.sync)
        {
            if (this.scheduler is null)
                return;

            var now = this.scheduler.GameTime;
            this.changed = true;
            while (this.queue.TryDequeue(out var tick, out _))
                this.pending.Add(new SavedBlockTick(tick.Position, tick.Block, (int)(tick.TriggerTick - now), tick.Priority));

            this.scheduler.Unregister(this);
            Volatile.Write(ref this.scheduler, null);
        }
    }

    /// <summary>
    /// The earliest due tick, if any.
    /// </summary>
    internal bool TryPeek(out ScheduledBlockTick tick)
    {
        lock (this.sync)
            return this.queue.TryPeek(out tick, out _);
    }

    /// <summary>
    /// Removes and returns the earliest due tick, if any; its position and block type can be scheduled again.
    /// </summary>
    internal bool TryPoll(out ScheduledBlockTick tick)
    {
        lock (this.sync)
        {
            if (!this.queue.TryDequeue(out tick, out _))
                return false;

            this.changed = true;
            this.scheduled.Remove((tick.Position, tick.Block));
            return true;
        }
    }
}
