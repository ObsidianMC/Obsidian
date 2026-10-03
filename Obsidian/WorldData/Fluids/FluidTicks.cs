namespace Obsidian.WorldData.Fluids;

/// <summary>
/// A fluid tick stored with its chunk, like vanilla's <c>SavedTick</c>: <see cref="Delay"/> counts from when the chunk
/// starts ticking.
/// </summary>
/// <param name="Priority">Vanilla's <c>TickPriority</c> (-3 to 3, lower runs first); fluids always use 0.</param>
internal readonly record struct SavedFluidTick(Vector Position, FluidKind Fluid, int Delay, int Priority = 0);

/// <summary>
/// A fluid tick due at an absolute game tick, like vanilla's <c>ScheduledTick</c>. Ticks due the same game tick run by
/// priority, then in the order they were scheduled (<see cref="SubTickOrder"/>).
/// </summary>
internal readonly record struct ScheduledFluidTick(Vector Position, FluidKind Fluid, long TriggerTick, int Priority, long SubTickOrder)
{
    /// <summary>
    /// Vanilla <c>ScheduledTick.DRAIN_ORDER</c>: by game tick, then priority, then scheduling order.
    /// </summary>
    public static Comparer<ScheduledFluidTick> DrainOrder { get; } = Comparer<ScheduledFluidTick>.Create((first, second) =>
    {
        var compare = first.TriggerTick.CompareTo(second.TriggerTick);
        return compare != 0 ? compare : IntraTickDrainOrder.Compare(first, second);
    });

    /// <summary>
    /// Vanilla <c>ScheduledTick.INTRA_TICK_DRAIN_ORDER</c>: by priority, then scheduling order.
    /// </summary>
    public static Comparer<ScheduledFluidTick> IntraTickDrainOrder { get; } = Comparer<ScheduledFluidTick>.Create((first, second) =>
    {
        var compare = first.Priority.CompareTo(second.Priority);
        return compare != 0 ? compare : first.SubTickOrder.CompareTo(second.SubTickOrder);
    });
}

/// <summary>
/// Vanilla's fluid registry names, which saved ticks use.
/// </summary>
internal static class FluidNames
{
    public static string Name(this FluidKind fluid) => fluid switch
    {
        FluidKind.Water => "minecraft:water",
        FluidKind.FlowingWater => "minecraft:flowing_water",
        FluidKind.Lava => "minecraft:lava",
        FluidKind.FlowingLava => "minecraft:flowing_lava",
        _ => "minecraft:empty"
    };

    public static FluidKind Parse(string? name) => name switch
    {
        "minecraft:water" => FluidKind.Water,
        "minecraft:flowing_water" => FluidKind.FlowingWater,
        "minecraft:lava" => FluidKind.Lava,
        "minecraft:flowing_lava" => FluidKind.FlowingLava,
        _ => FluidKind.Empty
    };
}
