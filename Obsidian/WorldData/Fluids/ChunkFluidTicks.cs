using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// A chunk's scheduled fluid ticks: vanilla's <c>ProtoChunkTicks</c> while the chunk generates (ticks wait with a delay
/// relative to when the chunk starts ticking) and its <c>LevelChunkTicks</c> once a level ticks it (ticks are due at
/// absolute game ticks).
/// </summary>
/// <remarks>
/// A position holds at most one tick per fluid; scheduling another is ignored, like vanilla. Thread-safe, since
/// generation, the level's tick and saving all reach it.
/// </remarks>
internal sealed class ChunkFluidTicks
{
    private readonly Lock sync = new();
    private readonly List<SavedFluidTick> pending = [];
    private readonly PriorityQueue<ScheduledFluidTick, ScheduledFluidTick> queue = new(ScheduledFluidTick.DrainOrder);
    private readonly HashSet<(Vector Position, FluidKind Fluid)> scheduled = [];
    private FluidTickScheduler? scheduler;

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
    /// Schedules a tick of <paramref name="fluid"/> at <paramref name="position"/> in <paramref name="delay"/> ticks, unless
    /// that fluid already has one there.
    /// </summary>
    public void Schedule(Vector position, FluidKind fluid, int delay)
    {
        lock (this.sync)
        {
            if (!this.scheduled.Add((position, fluid)))
                return;

            if (this.scheduler is null)
            {
                this.pending.Add(new SavedFluidTick(position, fluid, delay));
                return;
            }

            var tick = new ScheduledFluidTick(position, fluid, this.scheduler.GameTime + delay, 0, this.scheduler.NextSubTickOrder());
            this.queue.Enqueue(tick, tick);
            this.scheduler.OnTickAdded(this, tick);
        }
    }

    /// <summary>
    /// The ticks to save with the chunk, with delays relative to now (vanilla <c>pack</c>).
    /// </summary>
    /// <remarks>
    /// Waiting ticks come first, then due ones in the order they'd run (vanilla lists the latter in heap order).
    /// </remarks>
    public List<SavedFluidTick> Pack()
    {
        lock (this.sync)
        {
            var ticks = new List<SavedFluidTick>(this.pending);
            if (this.scheduler is not null)
            {
                var now = this.scheduler.GameTime;
                ticks.AddRange(this.queue.UnorderedItems.Select(entry => entry.Element).Order(ScheduledFluidTick.DrainOrder)
                    .Select(tick => new SavedFluidTick(tick.Position, tick.Fluid, (int)(tick.TriggerTick - now), tick.Priority)));
            }

            return ticks;
        }
    }

    /// <summary>
    /// Adds saved ticks before the chunk ticks, like vanilla loading a chunk's <c>fluid_ticks</c>.
    /// </summary>
    public void Load(IEnumerable<SavedFluidTick> ticks)
    {
        lock (this.sync)
        {
            foreach (var tick in ticks)
            {
                if (this.scheduled.Add((tick.Position, tick.Fluid)))
                    this.pending.Add(tick);
            }
        }
    }

    /// <summary>
    /// Reads vanilla's <c>fluid_ticks</c> list of chunk (<paramref name="chunkX"/>, <paramref name="chunkZ"/>): compounds
    /// with the fluid (<c>i</c>), position (<c>x</c>, <c>y</c>, <c>z</c>), delay (<c>t</c>) and priority (<c>p</c>).
    /// Like vanilla, ticks outside the chunk are dropped.
    /// </summary>
    public void Read(NbtList ticks, int chunkX, int chunkZ) => this.Load(ticks.OfType<NbtCompound>()
        .Select(tick => new SavedFluidTick(new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z")), FluidNames.Parse(tick.GetString("i")),
            tick.GetInt("t"), tick.GetInt("p")))
        .Where(tick => tick.Fluid != FluidKind.Empty && tick.Position.X >> 4 == chunkX && tick.Position.Z >> 4 == chunkZ));

    /// <summary>
    /// Writes the ticks as vanilla's <c>fluid_ticks</c> list.
    /// </summary>
    public void Write(NbtWriterStream writer)
    {
        var ticks = this.Pack();
        writer.WriteListStart("fluid_ticks", NbtTagType.Compound, ticks.Count);
        foreach (var tick in ticks)
        {
            writer.WriteCompoundStart();
            writer.WriteString("i", tick.Fluid.Name());
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
    internal void StartTicking(FluidTickScheduler ticker, long key)
    {
        lock (this.sync)
        {
            if (this.scheduler is not null)
                return;

            long order = -this.pending.Count;
            foreach (var saved in this.pending)
            {
                var tick = new ScheduledFluidTick(saved.Position, saved.Fluid, ticker.GameTime + saved.Delay, saved.Priority, order++);
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
                this.pending.Add(new SavedFluidTick(tick.Position, tick.Fluid, (int)(tick.TriggerTick - now), tick.Priority));

            this.scheduler.Unregister(this);
            Volatile.Write(ref this.scheduler, null);
        }
    }

    /// <summary>
    /// The earliest due tick, if any.
    /// </summary>
    internal bool TryPeek(out ScheduledFluidTick tick)
    {
        lock (this.sync)
            return this.queue.TryPeek(out tick, out _);
    }

    /// <summary>
    /// Removes and returns the earliest due tick, if any; its position and fluid can be scheduled again.
    /// </summary>
    internal bool TryPoll(out ScheduledFluidTick tick)
    {
        lock (this.sync)
        {
            if (!this.queue.TryDequeue(out tick, out _))
                return false;

            this.scheduled.Remove((tick.Position, tick.Fluid));
            return true;
        }
    }
}
