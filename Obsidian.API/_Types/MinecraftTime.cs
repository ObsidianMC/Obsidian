namespace Obsidian.API;
public readonly struct MinecraftTime
{
    public int? Day { get; private init; }

    public int? Second { get; private init; }

    public int? Tick { get; private init; }

    public static MinecraftTime FromDay(int day) => new()
    {
        Day = day,
        Second = day * 1200,
        Tick = day * 24000
    };

    public static MinecraftTime FromSecond(int second) => new()
    {
        Second = second,
        Tick = second * 20,
        Day = second / 1200
    };

    public static MinecraftTime FromTick(int tick) => new()
    { 
        Tick = tick,
        Second = tick / 20,
        Day = tick / 24000
    };

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
