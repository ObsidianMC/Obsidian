using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;
public readonly struct MinecraftTime
{
    public int? Day { get; private init; }

    public int? Second { get; private init; }

    public int? Tick { get; private init; }

    public static MinecraftTime FromDay(int day) => new() { Day = day };

    public static MinecraftTime FromSecond(int second) => new() { Second = second };

    public static MinecraftTime FromTick(int tick) => new() { Tick = tick };

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
