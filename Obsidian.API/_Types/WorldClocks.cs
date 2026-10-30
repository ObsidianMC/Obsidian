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
public sealed class WorldClockState
{
    public long TotalTicks { get; set; }

    public float PartialTick { get; set; }

    public float Rate { get; set; } = 1;

    public bool Paused { get; set; }

    /// <summary>Matches ServerClockInstance's single-precision accumulation and floor at each tick.</summary>
    public void Tick()
    {
        if (this.Paused)
            return;

        var accumulated = this.PartialTick + this.Rate;
        var elapsed = (int)MathF.Floor(accumulated);
        this.PartialTick = accumulated - elapsed;
        this.TotalTicks += elapsed;
    }

    public void SetTime(long ticks)
    {
        this.TotalTicks = ticks;
        this.PartialTick = 0;
    }
}
