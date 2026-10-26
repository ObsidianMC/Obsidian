namespace Obsidian.WorldData.BlockTicks;

/// <summary>
/// A level's block tick scheduler, like vanilla's <c>LevelTicks&lt;Block&gt;</c>: owns the game time ticks are scheduled
/// against and runs the due ticks of every ticking chunk in vanilla's order.
/// </summary>
/// <remarks>
/// Not thread-safe: the owner serializes ticking and scheduling.
/// </remarks>
internal sealed class BlockTickScheduler
{
    /// <summary>
    /// Vanilla's limit of block ticks run per game tick (<c>ServerLevel.tick</c> passes 65536 to <c>LevelTicks.tick</c>).
    /// </summary>
    public const int MaxTicksPerTick = 65536;

    private readonly Dictionary<long, ChunkBlockTicks> containers = [];
    private readonly Dictionary<long, long> nextTickForContainer = [];
    private readonly PriorityQueue<ChunkBlockTicks, ScheduledBlockTick> containersToTick = new(ScheduledBlockTick.IntraTickDrainOrder);
    private readonly Queue<ScheduledBlockTick> toRunThisTick = new();
    private readonly HashSet<(Vector Position, string Block)> runningThisTick = [];
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
    /// Advances the game time and runs the block ticks due by then, like the block stage of <c>ServerLevel.tick</c>.
    /// </summary>
    /// <param name="isTicking">Whether a chunk (by key) may tick now; others keep their ticks for later.</param>
    /// <param name="run">Runs one due tick (vanilla <c>ServerLevel.tickBlock</c>).</param>
    /// <param name="maxTicks">The per-tick limit; due ticks beyond it wait for the next game tick.</param>
    public void Tick(Func<long, bool> isTicking, Action<ScheduledBlockTick> run, int maxTicks = MaxTicksPerTick)
    {
        this.GameTime++;
        this.SortContainersToTick(isTicking);
        this.DrainContainers(maxTicks);

        // Containers left over when the limit was hit wait for the next tick.
        foreach (var (container, first) in this.containersToTick.UnorderedItems)
            this.nextTickForContainer[container.Key] = first.TriggerTick;

        this.containersToTick.Clear();
        while (this.toRunThisTick.TryDequeue(out var tick))
        {
            this.runningThisTick.Remove((tick.Position, tick.Block));
            run(tick);
        }
    }

    /// <summary>
    /// Whether a due tick of <paramref name="block"/> at <paramref name="position"/> is still to run this game tick (vanilla <c>willTickThisTick</c>).
    /// </summary>
    public bool WillTickThisTick(Vector position, string block) => this.runningThisTick.Contains((position, block));

    private void RunLater(ScheduledBlockTick tick)
    {
        this.toRunThisTick.Enqueue(tick);
        this.runningThisTick.Add((tick.Position, tick.Block));
    }

    internal void Register(ChunkBlockTicks container)
    {
        this.containers[container.Key] = container;
        if (container.TryPeek(out var first))
            this.nextTickForContainer[container.Key] = first.TriggerTick;
    }

    internal void Unregister(ChunkBlockTicks container)
    {
        // A newer container may have replaced it (the chunk was reloaded).
        if (this.containers.GetValueOrDefault(container.Key) == container)
        {
            this.containers.Remove(container.Key);
            this.nextTickForContainer.Remove(container.Key);
        }
    }

    // Vanilla's chunkScheduleUpdater: a new earliest tick moves the container's next tick.
    internal void OnTickAdded(ChunkBlockTicks container, ScheduledBlockTick tick)
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
            this.RunLater(tick);
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
    private void DrainFromCurrentContainer(ChunkBlockTicks container, int maxTicks)
    {
        var hasOther = this.containersToTick.TryPeek(out _, out var other);
        while (this.toRunThisTick.Count < maxTicks && container.TryPeek(out var tick) && tick.TriggerTick <= this.GameTime
            && (!hasOther || ScheduledBlockTick.IntraTickDrainOrder.Compare(tick, other) <= 0))
        {
            container.TryPoll(out _);
            this.RunLater(tick);
        }
    }
}
