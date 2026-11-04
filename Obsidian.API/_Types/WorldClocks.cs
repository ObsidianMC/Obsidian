using System.Threading;

namespace Obsidian.API;

/// <summary>A world's shared clocks, independent of the elapsed simulation time.</summary>
public sealed class WorldClocks
{
    public const string Overworld = "minecraft:overworld";

    public const string End = "minecraft:the_end";

    public long GameTime { get; set; }

    public Dictionary<string, WorldClockState> States { get; } = new(StringComparer.Ordinal)
    {
        [Overworld] = new(),
        [End] = new()
    };

    /// <summary>Advances game time once; the game rule gates clocks, including the End's, together.</summary>
    public void Tick(bool advanceTime, int clockTicks = 1)
    {
        this.GameTime++;
        if (!advanceTime)
            return;

        for (var tick = 0; tick < clockTicks; tick++)
        {
            foreach (var clock in this.States.Values)
                clock.Tick();
        }
    }
}

/// <summary>The persistent state of a 26.3 world clock.</summary>
/// <remarks>
/// A world's levels share its clocks, and commands change them from connection threads, so every change is atomic:
/// the world's tick, a level skipping the night and a command never split one another.
/// </remarks>
public sealed class WorldClockState
{
    private readonly Lock gate = new();

    private long totalTicks;

    public long TotalTicks
    {
        get => Volatile.Read(ref this.totalTicks);
        set
        {
            lock (this.gate)
                this.totalTicks = value;
        }
    }

    public float PartialTick { get; set; }

    public float Rate { get; set; } = 1;

    public bool Paused { get; set; }

    /// <summary>Matches ServerClockInstance's single-precision accumulation and floor at each tick.</summary>
    public void Tick()
    {
        lock (this.gate)
        {
            if (this.Paused)
                return;

            var accumulated = this.PartialTick + this.Rate;
            var elapsed = (int)MathF.Floor(accumulated);
            this.PartialTick = accumulated - elapsed;
            this.totalTicks += elapsed;
        }
    }

    public void SetTime(long ticks)
    {
        lock (this.gate)
        {
            this.totalTicks = ticks;
            this.PartialTick = 0;
        }
    }

    /// <summary>
    /// Sets the time from the time the clock has now, as one change: two levels skipping to the same marker at once
    /// skip once.
    /// </summary>
    /// <param name="change">
    /// The new time for the current one, or <c>null</c> to leave the clock as it is. A new time resets the partial
    /// tick, like <see cref="SetTime"/>.
    /// </param>
    /// <returns>The time set, or <c>null</c> when it was left as it is.</returns>
    public long? TrySetTime(Func<long, long?> change)
    {
        lock (this.gate)
        {
            if (change(this.totalTicks) is not long ticks)
                return null;

            this.totalTicks = ticks;
            this.PartialTick = 0;
            return ticks;
        }
    }

    /// <summary>
    /// Adds <paramref name="delta"/> ticks to the time, never going below 0, and keeps the partial tick.
    /// </summary>
    /// <returns>The new time.</returns>
    public long AddTicks(long delta)
    {
        lock (this.gate)
        {
            this.totalTicks = Math.Max(0, this.totalTicks + delta);
            return this.totalTicks;
        }
    }
}
