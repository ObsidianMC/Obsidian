namespace Obsidian.WorldData.Ticks;

/// <summary>
/// A tick stored with its chunk, like vanilla's <c>SavedTick</c>: <see cref="Delay"/> counts from when the chunk
/// starts ticking.
/// </summary>
/// <param name="Type">What ticks: a block's identifier, or a fluid.</param>
/// <param name="Priority">Vanilla's <c>TickPriority</c> (-3 to 3, lower runs first); fluids always use 0.</param>
internal readonly record struct SavedTick<T>(Vector Position, T Type, int Delay, int Priority = 0);

/// <summary>
/// A tick due at an absolute game tick, like vanilla's <c>ScheduledTick</c>. Ticks due the same game tick run by
/// priority, then in the order they were scheduled (<see cref="SubTickOrder"/>).
/// </summary>
internal readonly record struct ScheduledTick<T>(
    Vector Position,
    T Type,
    long TriggerTick,
    int Priority,
    long SubTickOrder)
{
    /// <summary>
    /// Vanilla <c>ScheduledTick.DRAIN_ORDER</c>: by game tick, then priority, then scheduling order.
    /// </summary>
    public static Comparer<ScheduledTick<T>> DrainOrder { get; } = Comparer<ScheduledTick<T>>.Create((first, second) =>
    {
        var compare = first.TriggerTick.CompareTo(second.TriggerTick);
        return compare != 0 ? compare : IntraTickDrainOrder.Compare(first, second);
    });

    /// <summary>
    /// Vanilla <c>ScheduledTick.INTRA_TICK_DRAIN_ORDER</c>: by priority, then scheduling order.
    /// </summary>
    public static Comparer<ScheduledTick<T>> IntraTickDrainOrder { get; } =
        Comparer<ScheduledTick<T>>.Create((first, second) =>
        {
            var compare = first.Priority.CompareTo(second.Priority);
            return compare != 0 ? compare : first.SubTickOrder.CompareTo(second.SubTickOrder);
        });

    /// <summary>
    /// The tick as saved at game tick <paramref name="now"/>.
    /// </summary>
    public SavedTick<T> ToSaved(long now) =>
        new(this.Position, this.Type, (int)(this.TriggerTick - now), this.Priority);
}
