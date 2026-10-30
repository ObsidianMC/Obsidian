using Obsidian.Commands.Framework;
using Obsidian.API.Commands;
using Obsidian.WorldData;
using System.Collections.Frozen;

namespace Obsidian.Commands.Modules;

[RequirePermissionLevel(2)]
public sealed class TimeCommandModule : CommandModuleBase
{
    private static readonly FrozenDictionary<string, System.Text.Json.JsonElement> timelines =
        ItemUseOnBehaviors.Load("timeline.json");

    [Command("time")]
    [CommandInfo("Changes world clocks", "/time [of <clock>] <set|add|pause|resume|rate|query> …")]
    public async Task<int> TimeAsync([Remaining] string arguments)
    {
        try
        {
            return await this.RunAsync(CommandInput.Arguments(this.CommandContext));
        }
        catch (FormatException exception)
        {
            await this.CommandContext.FailAsync(exception.Message);
            return 0;
        }
    }

    private async Task<int> RunAsync(CommandInput input)
    {
        var level = CommandFeedback.LevelOf(this.CommandContext);
        var action = input.Read();
        string? identifier = null;
        var explicitClock = action == "of";
        if (explicitClock)
        {
            identifier = CommandInput.Qualify(input.Read());
            action = input.Read();
        }

        var value = input.End ? null : input.Read();
        var repetition = !input.End;
        if (repetition)
            input.Expect("repetition");
        input.ExpectEnd();
        if (repetition && action != "query")
            throw new FormatException("Expected a clock query");

        if (action == "query" && value == "gametime" && !explicitClock && !repetition)
            return await this.ReportAsync(level.LevelData.WorldClocks.GameTime);

        identifier ??= WorldTimelines.DefaultClock(level);
        if (identifier is null)
            throw new FormatException("This dimension has no default clock; use /time of <clock>");

        if (!level.LevelData.WorldClocks.States.TryGetValue(identifier, out var clock))
            throw new FormatException($"Unknown clock {identifier}");

        int result;
        switch (action)
        {
            case "query":
                if (value == "time" && !repetition)
                    return await this.ReportAsync(clock.TotalTicks);

                var timeline = value is null ? string.Empty : CommandInput.Qualify(value);
                if (!timelines.TryGetValue(timeline, out var definition)
                    || definition.GetProperty("clock").GetString() != identifier)
                {
                    throw new FormatException($"Timeline {timeline} does not use clock {identifier}");
                }

                var period = definition.TryGetProperty("period_ticks", out var ticks) ? ticks.GetInt32() : 0;
                var time = clock.TotalTicks;
                if (period != 0)
                    time = repetition ? clock.TotalTicks / period : clock.TotalTicks % period;
                else if (repetition)
                    time = 0;

                return await this.ReportAsync(time);

            case "set":
                if (value is null || repetition)
                    throw new FormatException("Expected a time or time marker");

                if (char.IsDigit(value[0]) || value[0] is '-' or '+' or '.')
                {
                    var absolute = CommandInput.Ticks(value, 0);
                    if (clock.TotalTicks == absolute)
                        throw new FormatException($"Clock {identifier} is already at {absolute}");

                    clock.SetTime(absolute);
                    result = absolute;
                }
                else
                {
                    var markerName = CommandInput.Qualify(value);
                    if (!WorldTimelines.Markers.TryGetValue(markerName, out var marker) || marker.Clock != identifier)
                        throw new FormatException($"No time marker {markerName} for clock {identifier}");

                    if (marker.OccursAt(clock.TotalTicks))
                        throw new FormatException($"Clock {identifier} is already at {markerName}");

                    clock.SetTime(marker.Next(clock.TotalTicks));
                    result = Wrap(clock.TotalTicks);
                }

                break;

            case "add":
                if (value is null)
                    throw new FormatException("Expected a time");

                var delta = CommandInput.Ticks(value, int.MinValue);
                clock.TotalTicks = Math.Max(0, clock.TotalTicks + delta);
                result = Wrap(clock.TotalTicks);
                break;

            case "pause" or "resume":
                if (value is not null)
                    throw new FormatException("Unexpected clock argument");

                var paused = action == "pause";
                if (clock.Paused == paused)
                    throw new FormatException(paused ? "Clock is already paused" : "Clock is already running");

                clock.Paused = paused;
                result = 1;
                break;

            case "rate":
                var rate = CommandInput.Float(value ?? string.Empty);
                if (rate < 0.00001f || rate > 1000)
                {
                    throw new FormatException("Rate must be between 0.00001 and 1000");
                }

                if (clock.Rate == rate)
                    throw new FormatException("Clock already has this rate");

                clock.Rate = rate;
                result = 1;
                break;

            default:
                throw new FormatException("Unknown clock operation");
        }

        if (level is AbstractLevel concrete)
            concrete.SynchronizeClocks();

        await this.CommandContext.SuccessAsync(
            FormattableString.Invariant($"Updated {identifier}: time {clock.TotalTicks}, rate {clock.Rate}"));
        return result;
    }

    private async Task<int> ReportAsync(long ticks)
    {
        await this.CommandContext.SuccessAsync($"The time is {ticks}");
        return Wrap(ticks);
    }

    private static int Wrap(long ticks) => (int)(ticks % int.MaxValue);
}
