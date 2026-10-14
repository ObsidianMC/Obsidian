namespace Obsidian.WorldData.Ticks;

/// <summary>
/// A level's scheduler for one kind of tick (blocks or fluids, by <typeparamref name="T"/>), like vanilla's
/// <c>LevelTicks</c>: schedules ticks against the level's game time (its <see cref="Clock"/>) and runs the due ticks
/// of every ticking chunk in vanilla's order.
/// </summary>
/// <remarks>
/// Not thread-safe: the owner serializes ticking and scheduling.
/// </remarks>
internal sealed class TickScheduler<T>(TickClock clock) where T : notnull
{
    /// <summary>
    /// Vanilla's limit of ticks of each kind run per game tick.
    /// </summary>
    public const int MaxTicksPerTick = 65536;

    private readonly Dictionary<long, ChunkTicks<T>> containers = [];
    private readonly Dictionary<long, long> nextTickForContainer = [];
    private readonly PriorityQueue<ChunkTicks<T>, ScheduledTick<T>> containersToTick =
        new(ScheduledTick<T>.IntraTickDrainOrder);
    private readonly Queue<(ChunkTicks<T> Container, ScheduledTick<T> Tick)> toRunThisTick = new();
    private readonly HashSet<(Vector Position, T Type)> toRunThisTickSet = [];
    private long subTickCount;

    /// <summary>
    /// A scheduler with a clock of its own.
    /// </summary>
    public TickScheduler()
        : this(new TickClock())
    {
    }

    /// <summary>
    /// The game time ticks are scheduled against, which the owner advances before running each game tick's ticks.
    /// </summary>
    public TickClock Clock => clock;

    /// <summary>
    /// The current game tick, of <see cref="Clock"/>.
    /// </summary>
    public long GameTime => clock.GameTime;

    /// <summary>
    /// The key of the chunk holding <paramref name="position"/>.
    /// </summary>
    public static long ChunkKey(Vector position) => NumericsHelper.IntsToLong(position.X >> 4, position.Z >> 4);

    /// <summary>
    /// The scheduling order of the next tick scheduled (vanilla <c>Level.nextSubTickCount</c>).
    /// </summary>
    public long NextSubTickOrder() => this.subTickCount++;

    /// <summary>
    /// Whether a tick of <paramref name="type"/> at <paramref name="position"/> is due this game tick and hasn't run
    /// yet, like vanilla's <c>willTickThisTick</c>: only while the due ticks run. Such a tick is no longer scheduled,
    /// so redstone components check this before scheduling one again.
    /// </summary>
    public bool WillTickThisTick(Vector position, T type) => this.toRunThisTickSet.Contains((position, type));

    /// <summary>
    /// Runs the ticks due by the clock's game time, like <c>LevelTicks.tick</c>; advance the clock first.
    /// </summary>
    /// <param name="isTicking">Whether a chunk (by key) may tick now; others keep their ticks for later.</param>
    /// <param name="run">Runs one tick (vanilla <c>ServerLevel.tickBlock</c> or <c>tickFluid</c>).</param>
    /// <param name="maxTicks">The most ticks to run.</param>
    public void Tick(Func<long, bool> isTicking, Action<Vector, T> run, int maxTicks = MaxTicksPerTick) =>
        this.Tick(
            isTicking,
            (position, type) =>
            {
                run(position, type);
                return true;
            },
            maxTicks);

    /// <summary>
    /// Runs the ticks due by the clock's game time, like <c>LevelTicks.tick</c>; advance the clock first.
    /// </summary>
    /// <param name="isTicking">Whether a chunk (by key) may tick now; others keep their ticks for later.</param>
    /// <param name="run">
    /// Runs one tick, or returns false to hold it: the tick stays scheduled as it was, with its game tick, priority
    /// and scheduling order, so that it's due again next game tick, in its place among the others.
    /// </param>
    /// <param name="maxTicks">The most ticks to run.</param>
    public void Tick(Func<long, bool> isTicking, Func<Vector, T, bool> run, int maxTicks = MaxTicksPerTick)
    {
        this.SortContainersToTick(isTicking);
        this.DrainContainers(maxTicks);

        // Containers left over when the limit was hit wait for the next tick.
        foreach (var (container, first) in this.containersToTick.UnorderedItems)
            this.nextTickForContainer[container.Key] = first.TriggerTick;

        this.containersToTick.Clear();
        foreach (var (_, tick) in this.toRunThisTick)
            this.toRunThisTickSet.Add((tick.Position, tick.Type));

        while (this.toRunThisTick.TryDequeue(out var entry))
        {
            this.toRunThisTickSet.Remove((entry.Tick.Position, entry.Tick.Type));
            if (!run(entry.Tick.Position, entry.Tick.Type))
                entry.Container.Hold(entry.Tick, this.GameTime);
        }
    }

    internal void Register(ChunkTicks<T> container)
    {
        this.containers[container.Key] = container;
        if (container.TryPeek(out var first))
            this.nextTickForContainer[container.Key] = first.TriggerTick;
    }

    internal void Unregister(ChunkTicks<T> container)
    {
        // A newer container may have replaced it (the chunk was reloaded).
        if (this.containers.GetValueOrDefault(container.Key) == container)
        {
            this.containers.Remove(container.Key);
            this.nextTickForContainer.Remove(container.Key);
        }
    }

    // Vanilla's chunkScheduleUpdater: a new earliest tick moves the container's next tick.
    internal void OnTickAdded(ChunkTicks<T> container, ScheduledTick<T> tick)
    {
        if (container.TryPeek(out var first) && first == tick)
            this.nextTickForContainer[container.Key] = tick.TriggerTick;
    }

    private void SortContainersToTick(Func<long, bool> isTicking)
    {
        foreach (var (key, next) in this.nextTickForContainer.ToArray())
        {
            if (next > this.GameTime)
                continue;

            var container = this.containers.GetValueOrDefault(key);
            if (container is null || !container.TryPeek(out var first))
            {
                this.nextTickForContainer.Remove(key);
            }
            else if (first.TriggerTick > this.GameTime)
            {
                this.nextTickForContainer[key] = first.TriggerTick;
            }
            else if (isTicking(key))
            {
                this.nextTickForContainer.Remove(key);
                this.containersToTick.Enqueue(container, first);
            }
        }
    }

    private void DrainContainers(int maxTicks)
    {
        while (this.toRunThisTick.Count < maxTicks && this.containersToTick.TryDequeue(out var container, out _))
        {
            container.TryPoll(out var tick);
            this.toRunThisTick.Enqueue((container, tick));
            this.DrainFromCurrentContainer(container, maxTicks);

            if (container.TryPeek(out var next))
            {
                if (next.TriggerTick <= this.GameTime && this.toRunThisTick.Count < maxTicks)
                    this.containersToTick.Enqueue(container, next);
                else
                    this.nextTickForContainer[container.Key] = next.TriggerTick;
            }
        }
    }

    // Takes the container's due ticks for as long as they come before the next container's first tick.
    private void DrainFromCurrentContainer(ChunkTicks<T> container, int maxTicks)
    {
        var hasOther = this.containersToTick.TryPeek(out _, out var other);
        while (this.toRunThisTick.Count < maxTicks
            && container.TryPeek(out var tick)
            && tick.TriggerTick <= this.GameTime
            && (!hasOther || ScheduledTick<T>.IntraTickDrainOrder.Compare(tick, other) <= 0))
        {
            container.TryPoll(out _);
            this.toRunThisTick.Enqueue((container, tick));
        }
    }
}
