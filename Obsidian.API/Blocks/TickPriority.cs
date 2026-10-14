namespace Obsidian.API.Blocks;

/// <summary>
/// Which of the block ticks due the same game tick run first, like vanilla's <c>TickPriority</c>: lower first, then in
/// the order they were scheduled. Saved with the tick, as vanilla saves it.
/// </summary>
public enum TickPriority
{
    ExtremelyHigh = -3,
    VeryHigh = -2,
    High = -1,
    Normal = 0,
    Low = 1,
    VeryLow = 2,
    ExtremelyLow = 3
}
