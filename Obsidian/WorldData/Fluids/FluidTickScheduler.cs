namespace Obsidian.WorldData.Fluids;

/// <summary>
/// A level's fluid tick scheduler, like vanilla's <c>LevelTicks&lt;Fluid&gt;</c>: owns the game time ticks are scheduled
/// against and runs the due ticks of every ticking chunk in vanilla's order.
/// </summary>
/// <remarks>
/// Not thread-safe: the owner serializes ticking and scheduling.
/// </remarks>
internal sealed class FluidTickScheduler
{
    /// <summary>
    /// Vanilla's limit of fluid ticks run per game tick.
    /// </summary>
    public const int MaxTicksPerTick = 65536;

    private readonly Dictionary<long, ChunkFluidTicks> containers = [];
    private readonly Dictionary<long, long> nextTickForContainer = [];
    private readonly PriorityQueue<ChunkFluidTicks, ScheduledFluidTick> containersToTick = new(ScheduledFluidTick.IntraTickDrainOrder);
    private readonly Queue<ScheduledFluidTick> toRunThisTick = new();
    private long subTickCount;

    /// <summary>
    /// The game tick, which advances once per <see cref="Tick"/>.
    /// </summary>
    public long GameTime { get; private set; }

    /// <summary>
    /// The key of the chunk holding <paramref name="position"/>.
    /// </summary>
    public static long ChunkKey(Vector position) => NumericsHelper.IntsToLong(position.X >> 4, position.Z >> 4);

    /// <summary>
    /// The scheduling order of the next tick scheduled (vanilla <c>Level.nextSubTickCount</c>).
    /// </summary>
    public long NextSubTickOrder() => this.subTickCount++;

    /// <summary>
    /// Advances the game time and runs the fluid ticks due by then, like <c>ServerLevel.tick</c>.
    /// </summary>
    /// <param name="isTicking">Whether a chunk (by key) may tick now; others keep their ticks for later.</param>
    /// <param name="run">Runs one tick (vanilla <c>ServerLevel.tickFluid</c>).</param>
    public void Tick(Func<long, bool> isTicking, Action<Vector, FluidKind> run, int maxTicks = MaxTicksPerTick)
    {
        this.GameTime++;
        this.SortContainersToTick(isTicking);
        this.DrainContainers(maxTicks);

        // Containers left over when the limit was hit wait for the next tick.
        foreach (var (container, first) in this.containersToTick.UnorderedItems)
            this.nextTickForContainer[container.Key] = first.TriggerTick;

        this.containersToTick.Clear();
        while (this.toRunThisTick.TryDequeue(out var tick))
            run(tick.Position, tick.Fluid);
    }

    internal void Register(ChunkFluidTicks container)
    {
        this.containers[container.Key] = container;
        if (container.TryPeek(out var first))
            this.nextTickForContainer[container.Key] = first.TriggerTick;
    }

    internal void Unregister(ChunkFluidTicks container)
    {
        // A newer container may have replaced it (the chunk was reloaded).
        if (this.containers.GetValueOrDefault(container.Key) == container)
        {
            this.containers.Remove(container.Key);
            this.nextTickForContainer.Remove(container.Key);
        }
    }

    // Vanilla's chunkScheduleUpdater: a new earliest tick moves the container's next tick.
    internal void OnTickAdded(ChunkFluidTicks container, ScheduledFluidTick tick)
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
            this.toRunThisTick.Enqueue(tick);
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
    private void DrainFromCurrentContainer(ChunkFluidTicks container, int maxTicks)
    {
        var hasOther = this.containersToTick.TryPeek(out _, out var other);
        while (this.toRunThisTick.Count < maxTicks && container.TryPeek(out var tick) && tick.TriggerTick <= this.GameTime
            && (!hasOther || ScheduledFluidTick.IntraTickDrainOrder.Compare(tick, other) <= 0))
        {
            container.TryPoll(out _);
            this.toRunThisTick.Enqueue(tick);
        }
    }
}
