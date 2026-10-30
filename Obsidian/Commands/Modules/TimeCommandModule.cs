using Obsidian.API.Commands;
using Obsidian.WorldData;
using System.Globalization;

namespace Obsidian.Commands.Modules;

[CommandGroup("time")]
[CommandInfo("Changes world clocks", "/time <set|add|pause|resume|rate|query|of>")]
[RequirePermission(permissions: "time")]
public sealed class TimeCommandModule : CommandModuleBase
{
    private ILevel Level => this.Player?.Level ?? this.Server.DefaultWorld;

    [Command("query")]
    public async Task Query(string value = "time")
    {
        if (value == "gametime")
        {
            await this.Sender.SendMessageAsync($"The time is {this.Level.LevelData.WorldClocks.GameTime}");
            return;
        }

        await this.ExecuteAsync(null, "query", value);
    }

    [Command("set")]
    public Task SetTime(MinecraftTime time) => this.ExecuteAsync(null, "set", time.Tick?.ToString());

    [CommandOverload]
    public Task SetTime(string value) => this.ExecuteAsync(null, "set", value);

    [Command("add")]
    public Task AddTime(int ticks) => this.ExecuteAsync(null, "add", ticks.ToString(CultureInfo.InvariantCulture));

    [Command("pause")]
    public Task Pause() => this.ExecuteAsync(null, "pause", null);

    [Command("resume")]
    public Task Resume() => this.ExecuteAsync(null, "resume", null);

    [Command("rate")]
    public Task Rate(float rate) => this.ExecuteAsync(null, "rate", rate.ToString(CultureInfo.InvariantCulture));

    [Command("of")]
    public Task Of(string clock, string action, string? value = null) => this.ExecuteAsync(clock, action, value);

    private async Task ExecuteAsync(string? identifier, string action, string? value)
    {
        identifier ??= WorldTimelines.DefaultClock(this.Level);
        if (identifier is null)
        {
            await this.Sender.SendMessageAsync("This dimension has no default clock; use /time of <clock>.");
            return;
        }

        if (!identifier.Contains(':'))
            identifier = "minecraft:" + identifier;

        if (!this.Level.LevelData.WorldClocks.States.TryGetValue(identifier, out var clock))
        {
            await this.Sender.SendMessageAsync($"Unknown clock {identifier}.");
            return;
        }

        if (action == "query")
        {
            var result = value switch
            {
                "daytime" => clock.TotalTicks % 24000,
                "day" => clock.TotalTicks / 24000,
                _ => clock.TotalTicks
            };
            await this.Sender.SendMessageAsync($"The time is {result}");
            return;
        }

        if (!Change(clock, identifier, action, value))
        {
            await this.Sender.SendMessageAsync("Invalid clock operation or value.");
            return;
        }

        if (this.Level is AbstractLevel level)
            level.SynchronizeClocks();

        await this.Sender.SendMessageAsync($"Updated {identifier}: time {clock.TotalTicks}, rate {clock.Rate}.");
    }

    private static bool Change(WorldClockState clock, string identifier, string action, string? value)
    {
        switch (action)
        {
            case "pause":
                clock.Paused = true;
                return true;

            case "resume":
                clock.Paused = false;
                return true;

            case "rate":
                if (!float.TryParse(value, CultureInfo.InvariantCulture, out var rate)
                    || !float.IsFinite(rate)
                    || rate <= 0
                    || rate > 1000)
                    return false;

                clock.Rate = rate;
                return true;

            case "add":
                if (!int.TryParse(value, CultureInfo.InvariantCulture, out var delta))
                    return false;

                // 26.3 ServerClockManager.addTicks preserves the fractional tick and clamps at zero.
                clock.TotalTicks = Math.Max(0, clock.TotalTicks + delta);
                return true;

            case "set":
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var ticks) && ticks >= 0)
                {
                    clock.SetTime(ticks);
                    return true;
                }

                var markerName = value;
                if (markerName is not null && !markerName.Contains(':'))
                    markerName = "minecraft:" + markerName;

                if (markerName is null
                    || !WorldTimelines.Markers.TryGetValue(markerName, out var marker)
                    || marker.Clock != identifier)
                    return false;

                clock.SetTime(marker.Next(clock.TotalTicks));
                return true;

            default:
                return false;
        }
    }
}
