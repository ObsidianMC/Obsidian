using Obsidian.API.Commands;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Collections.Frozen;

namespace Obsidian.Commands.Modules;

[CommandGroup("time")]
[CommandInfo("Sets declared time", "/time <set|add> (value)")]
[RequirePermission(permissions: "time")]
public sealed class TimeCommandModule : CommandModuleBase
{
    private const int Mod = 24_000;
    private static readonly FrozenDictionary<string, int> TimeDictionary = new Dictionary<string, int>()
        {
            { "day", 1000 },
            { "night", 13_000 },
            { "noon", 6000  },
            { "midnight", 18_000 }
        }.ToFrozenDictionary();

    [Command("query")]
    [CommandInfo("Queries the time", "/time query <day|daytime|gametime>")]
    public async Task Query(string value)
    {
        switch (value)
        {
            case "daytime":
                await this.Sender.SendMessageAsync($"The time is {this.Server.DefaultWorld.DayTime}");
                break;
            case "day":
                await this.Sender.SendMessageAsync($"The time is {(int)(this.Server.DefaultWorld.Time / Mod)}");
                break;
            case "gametime":
                await this.Sender.SendMessageAsync($"The time is {this.Server.DefaultWorld.Time}");
                break;
            default:
                await this.Sender.SendMessageAsync("Invalid value.");
                break;
        }
    }

    [Command("set")]
    [CommandInfo("Sets declared time", "/time set <(d|t|s)>")]
    public async Task SetTime(MinecraftTime time)
    {
        if (time.ConvertServerTime(this.Server))
            await this.Sender.SendMessageAsync($"Set the time to {this.Server.DefaultWorld.Time}");
        else
            await this.Sender.SendMessageAsync("Failed to set the time.");
    }

    //TODO: Command Suggestions
    [Command("set")]
    [CommandOverload]
    [CommandInfo("Sets declared time", "/time set <day|night|noon|midnight>")]
    public async Task SetTime(string value)
    {
        if (TimeDictionary.TryGetValue(value, out int time))
        {
            this.Server.DefaultWorld.DayTime = time;

            await this.Sender.SendMessageAsync($"Set the time to {value}");

            return;
        }

        await this.Sender.SendMessageAsync($"{value} is an invalid argument value.");
    }

    [Command("add")]
    public async Task AddTime(int timeToAdd)
    {
        this.Server.DefaultWorld.DayTime += timeToAdd;

        await this.Sender.SendMessageAsync($"Set the time to {this.Server.DefaultWorld.Time}");
    }
}
