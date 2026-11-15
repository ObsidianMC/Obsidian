namespace Obsidian.API.Blocks;

/// <summary>
/// Which of the block ticks due the same game tick run first, like vanilla's <c>TickPriority</c>: lower first, then in
/// the order they were scheduled. Saved with the tick, as vanilla saves it.
/// </summary>
public enum TickPriority
{
    /// <summary>
    /// Runs before all other priority levels due on the same tick.
    /// </summary>
    ExtremelyHigh = -3,
    /// <summary>
    /// Runs after extremely high priority and before high priority.
    /// </summary>
    VeryHigh = -2,
    /// <summary>
    /// Runs before normal priority ticks.
    /// </summary>
    High = -1,
    /// <summary>
    /// Uses the default scheduled tick priority.
    /// </summary>
    Normal = 0,
    /// <summary>
    /// Runs after normal priority ticks.
    /// </summary>
    Low = 1,
    /// <summary>
    /// Runs after low priority and before extremely low priority.
    /// </summary>
    VeryLow = 2,
    /// <summary>
    /// Runs after all other priority levels due on the same tick.
    /// </summary>
    ExtremelyLow = 3
}
