namespace Obsidian.WorldData.Ticks;

/// <summary>
/// The game time a level's tick schedulers share, like vanilla's level game time: both block and fluid ticks are
/// scheduled against it, so a fluid tick a block tick schedules counts from the same game tick as a block tick would.
/// </summary>
/// <remarks>
/// Not thread-safe: the owner advances it and schedules ticks under one lock.
/// </remarks>
internal sealed class TickClock
{
    /// <summary>
    /// The current game tick.
    /// </summary>
    public long GameTime { get; private set; }

    /// <summary>
    /// Moves to the next game tick; call it before any scheduler sharing the clock runs that tick's ticks.
    /// </summary>
    public void Advance() => this.GameTime++;
}
