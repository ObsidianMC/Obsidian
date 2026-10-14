using Obsidian.Nbt;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.WorldData.Ticks;

/// <summary>
/// Reads the type of a saved tick from its name, failing for names the tick list doesn't keep.
/// </summary>
internal delegate bool TickTypeParser<T>(string name, [MaybeNullWhen(false)] out T type);

/// <summary>
/// A chunk's scheduled ticks of one kind (blocks or fluids, by <typeparamref name="T"/>): vanilla's
/// <c>ProtoChunkTicks</c> while the chunk generates (ticks wait with a delay relative to when the chunk starts
/// ticking) and its <c>LevelChunkTicks</c> once a level ticks it (ticks are due at absolute game ticks).
/// </summary>
/// <remarks>
/// A position holds at most one tick per type; scheduling another is ignored, like vanilla. Thread-safe, since
/// generation, the level's tick and saving all reach it.
/// </remarks>
internal sealed class ChunkTicks<T> where T : notnull
{
    private readonly Lock sync = new();
    private readonly List<SavedTick<T>> pending = [];
    private readonly PriorityQueue<ScheduledTick<T>, ScheduledTick<T>> queue = new(ScheduledTick<T>.DrainOrder);
    private readonly HashSet<(Vector Position, T Type)> scheduled = [];
    private TickScheduler<T>? scheduler;

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
    /// Whether a level ticks the chunk (its ticks are due at absolute game ticks).
    /// </summary>
    public bool IsTicking => Volatile.Read(ref this.scheduler) is not null;

    /// <summary>
    /// The chunk's key in the scheduler ticking it.
    /// </summary>
    internal long Key { get; private set; }

    /// <summary>
    /// Schedules a tick of <paramref name="type"/> at <paramref name="position"/> in <paramref name="delay"/> ticks,
    /// unless that type already has one there.
    /// </summary>
    public void Schedule(Vector position, T type, int delay, int priority = 0)
    {
        lock (this.sync)
        {
            if (!this.scheduled.Add((position, type)))
                return;

            if (this.scheduler is null)
            {
                this.pending.Add(new SavedTick<T>(position, type, delay, priority));
                return;
            }

            var tick = new ScheduledTick<T>(
                position,
                type,
                this.scheduler.GameTime + delay,
                priority,
                this.scheduler.NextSubTickOrder());

            this.queue.Enqueue(tick, tick);
            this.scheduler.OnTickAdded(this, tick);
        }
    }

    /// <summary>
    /// Whether a tick of <paramref name="type"/> is scheduled at <paramref name="position"/>.
    /// </summary>
    public bool Contains(Vector position, T type)
    {
        lock (this.sync)
            return this.scheduled.Contains((position, type));
    }

    /// <summary>
    /// The ticks to save with the chunk, with delays relative to now (vanilla <c>pack</c>).
    /// </summary>
    /// <remarks>
    /// Waiting ticks come first, then due ones in the order they'd run (vanilla lists the latter in heap order).
    /// </remarks>
    public List<SavedTick<T>> Pack()
    {
        lock (this.sync)
        {
            var ticks = new List<SavedTick<T>>(this.pending);
            if (this.scheduler is not null)
            {
                var now = this.scheduler.GameTime;
                var queued = this.queue.UnorderedItems
                    .Select(entry => entry.Element)
                    .Order(ScheduledTick<T>.DrainOrder)
                    .Select(tick => tick.ToSaved(now));

                ticks.AddRange(queued);
            }

            return ticks;
        }
    }

    /// <summary>
    /// Adds saved ticks before the chunk ticks, like vanilla loading a chunk's tick list.
    /// </summary>
    public void Load(IEnumerable<SavedTick<T>> ticks)
    {
        lock (this.sync)
        {
            foreach (var tick in ticks)
            {
                if (this.scheduled.Add((tick.Position, tick.Type)))
                    this.pending.Add(tick);
            }
        }
    }

    /// <summary>
    /// Reads one of vanilla's tick lists (<c>block_ticks</c> or <c>fluid_ticks</c>) of chunk
    /// (<paramref name="chunkX"/>, <paramref name="chunkZ"/>): compounds with the type (<c>i</c>), position
    /// (<c>x</c>, <c>y</c>, <c>z</c>), delay (<c>t</c>) and priority (<c>p</c>). Like vanilla, ticks outside the
    /// chunk, and ticks of types <paramref name="parse"/> doesn't know, are dropped.
    /// </summary>
    public void Read(NbtList ticks, int chunkX, int chunkZ, TickTypeParser<T> parse)
    {
        var read = new List<SavedTick<T>>();
        foreach (var tick in ticks.OfType<NbtCompound>())
        {
            var position = new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z"));
            if (position.X >> 4 != chunkX || position.Z >> 4 != chunkZ)
                continue;

            if (tick.GetString("i") is string name && parse(name, out var type))
                read.Add(new SavedTick<T>(position, type, tick.GetInt("t"), tick.GetInt("p")));
        }

        this.Load(read);
    }

    /// <summary>
    /// Writes the ticks as vanilla's tick list <paramref name="listName"/>, naming their types with
    /// <paramref name="name"/>.
    /// </summary>
    public void Write(NbtWriterStream writer, string listName, Func<T, string> name)
    {
        var ticks = this.Pack();
        writer.WriteListStart(listName, NbtTagType.Compound, ticks.Count);
        foreach (var tick in ticks)
        {
            writer.WriteCompoundStart();
            writer.WriteString("i", name(tick.Type));
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
    /// Hands the ticks to <paramref name="ticker"/>, which runs them from now on (vanilla registering the container
    /// and unpacking it when the chunk starts ticking).
    /// </summary>
    /// <remarks>
    /// Saved ticks get negative scheduling orders, so they run before ticks scheduled the same game tick, like
    /// vanilla.
    /// </remarks>
    internal void StartTicking(TickScheduler<T> ticker, long key)
    {
        lock (this.sync)
        {
            if (this.scheduler is not null)
                return;

            long order = -this.pending.Count;
            foreach (var saved in this.pending)
            {
                var tick = new ScheduledTick<T>(
                    saved.Position,
                    saved.Type,
                    ticker.GameTime + saved.Delay,
                    saved.Priority,
                    order++);

                this.queue.Enqueue(tick, tick);
            }

            this.pending.Clear();
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
            while (this.queue.TryDequeue(out var tick, out _))
                this.pending.Add(tick.ToSaved(now));

            this.scheduler.Unregister(this);
            Volatile.Write(ref this.scheduler, null);
        }
    }

    /// <summary>
    /// The earliest due tick, if any.
    /// </summary>
    internal bool TryPeek(out ScheduledTick<T> tick)
    {
        lock (this.sync)
            return this.queue.TryPeek(out tick, out _);
    }

    /// <summary>
    /// Removes and returns the earliest due tick, if any; its position and type can be scheduled again.
    /// </summary>
    internal bool TryPoll(out ScheduledTick<T> tick)
    {
        lock (this.sync)
        {
            if (!this.queue.TryDequeue(out tick, out _))
                return false;

            this.scheduled.Remove((tick.Position, tick.Type));
            return true;
        }
    }
}
