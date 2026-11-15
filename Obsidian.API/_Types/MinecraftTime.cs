namespace Obsidian.API;
/// <summary>An amount of game time in days, seconds and ticks. A day is 24000 ticks, or 1200 seconds.</summary>
public readonly record struct MinecraftTime
{
    /// <summary>Gets the time in whole days.</summary>
    public int? Day { get; private init; }

    /// <summary>Gets the time in whole seconds.</summary>
    public int? Second { get; private init; }

    /// <summary>Gets the time in ticks.</summary>
    public int? Tick { get; private init; }

    /// <summary>Creates a time from a number of days.</summary>
    public static MinecraftTime FromDay(int day) => new()
    {
        Day = day,
        Second = day * 1200,
        Tick = day * 24000
    };

    /// <summary>Creates a time from a number of seconds; <see cref="Day"/> is truncated toward zero.</summary>
    public static MinecraftTime FromSecond(int second) => new()
    {
        Second = second,
        Tick = second * 20,
        Day = second / 1200
    };

    /// <summary>
    /// Creates a time from a number of ticks; <see cref="Second"/> and <see cref="Day"/> are truncated toward zero.
    /// </summary>
    public static MinecraftTime FromTick(int tick) => new()
    { 
        Tick = tick,
        Second = tick / 20,
        Day = tick / 24000
    };

    /// <summary>
    /// Sets the default world's time to this time, using <see cref="Day"/> if it has a value, else
    /// <see cref="Second"/>, else <see cref="Tick"/>.
    /// </summary>
    /// <remarks>A time made by <see cref="FromSecond"/> or <see cref="FromTick"/> has <see cref="Day"/> set, so it is
    /// applied in whole days.</remarks>
    /// <returns>Whether any of the three had a value.</returns>
    public bool ConvertServerTime(IServer server)
    {
        var success = false;

        if (this.Day.HasValue)
        {
            server.DefaultWorld.Time = this.Day.Value * 24000;
            success = true;
        }
        else if (this.Second.HasValue)
        {
            server.DefaultWorld.Time = this.Second.Value * 20;
            success = true;
        }
        else if (this.Tick.HasValue)
        {
            server.DefaultWorld.Time = this.Tick.Value;
            success = true;
        }

        return success;
    }
}
