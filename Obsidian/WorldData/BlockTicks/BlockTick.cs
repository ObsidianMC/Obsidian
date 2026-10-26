namespace Obsidian.WorldData.BlockTicks;

/// <summary>
/// Vanilla's <c>TickPriority</c> values. Lower values run first among ticks due the same game tick.
/// </summary>
public static class TickPriority
{
    public const int ExtremelyHigh = -3;
    public const int VeryHigh = -2;
    public const int High = -1;
    public const int Normal = 0;
    public const int Low = 1;
    public const int VeryLow = 2;
    public const int ExtremelyLow = 3;

    /// <summary>
    /// Whether <paramref name="priority"/> is one of vanilla's values.
    /// </summary>
    public static bool IsValid(int priority) => priority is >= ExtremelyHigh and <= ExtremelyLow;
}

/// <summary>
/// A block tick stored with its chunk, like vanilla's <c>SavedTick</c>: <see cref="Delay"/> counts from when the chunk
/// starts ticking.
/// </summary>
/// <param name="Position">The block the tick is for.</param>
/// <param name="Block">The block's unlocalized name when the tick was scheduled, like vanilla's block type.</param>
/// <param name="Delay">Game ticks until the tick is due.</param>
/// <param name="Priority">Vanilla's <c>TickPriority</c>.</param>
internal readonly record struct SavedBlockTick(Vector Position, string Block, int Delay, int Priority = TickPriority.Normal);

/// <summary>
/// A block tick due at an absolute game tick, like vanilla's <c>ScheduledTick</c>. Ticks due the same game tick run by
/// priority, then in the order they were scheduled (<see cref="SubTickOrder"/>).
/// </summary>
internal readonly record struct ScheduledBlockTick(Vector Position, string Block, long TriggerTick, int Priority, long SubTickOrder)
{
    /// <summary>
    /// Vanilla <c>ScheduledTick.DRAIN_ORDER</c>: by game tick, then priority, then scheduling order.
    /// </summary>
    public static Comparer<ScheduledBlockTick> DrainOrder { get; } = Comparer<ScheduledBlockTick>.Create((first, second) =>
    {
        var compare = first.TriggerTick.CompareTo(second.TriggerTick);
        return compare != 0 ? compare : IntraTickDrainOrder.Compare(first, second);
    });

    /// <summary>
    /// Vanilla <c>ScheduledTick.INTRA_TICK_DRAIN_ORDER</c>: by priority, then scheduling order.
    /// </summary>
    public static Comparer<ScheduledBlockTick> IntraTickDrainOrder { get; } = Comparer<ScheduledBlockTick>.Create((first, second) =>
    {
        var compare = first.Priority.CompareTo(second.Priority);
        return compare != 0 ? compare : first.SubTickOrder.CompareTo(second.SubTickOrder);
    });
}
