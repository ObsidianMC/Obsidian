using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>Gameplay tracks and time markers from the vanilla timeline registry.</summary>
internal static class WorldTimelines
{
    private static readonly FrozenDictionary<string, JsonElement> definitions =
        ItemUseOnBehaviors.Load("timeline.json");

    internal static FrozenDictionary<string, TimeMarker> Markers { get; } = LoadMarkers();

    internal static string? DefaultClock(ILevel level) => level.DimensionType.Element.DefaultClock
        ?? level.DimensionType.Name switch
        {
            "minecraft:overworld" or "minecraft:overworld_caves" => WorldClocks.Overworld,
            "minecraft:the_end" => WorldClocks.End,
            _ => null
        };

    internal sealed record TimeMarker(string Clock, int Ticks, int? Period, bool ShowInCommands)
    {
        public long Next(long time)
        {
            if (Period is not int period)
                return Ticks;

            var offset = Ticks - time % period;
            if (offset <= 0)
                offset += period;

            return time + offset;
        }

        public bool OccursAt(long time) => Period is int period ? time % period == Ticks : time == Ticks;
    }

    private static FrozenDictionary<string, TimeMarker> LoadMarkers()
    {
        var markers = new Dictionary<string, TimeMarker>();
        foreach (var definition in definitions.Values)
        {
            if (!definition.TryGetProperty("time_markers", out var values))
                continue;

            var clock = definition.GetProperty("clock").GetString()!;
            int? period = definition.TryGetProperty("period_ticks", out var ticks) ? ticks.GetInt32() : null;
            foreach (var entry in values.EnumerateObject())
            {
                var value = entry.Value;
                var time = value.ValueKind == JsonValueKind.Number
                    ? value.GetInt32()
                    : value.GetProperty("ticks").GetInt32();
                var show = value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty("show_in_commands", out var visible)
                    && visible.GetBoolean();
                markers.Add(entry.Name, new TimeMarker(clock, time, period, show));
            }
        }
        return markers.ToFrozenDictionary();
    }

    private static IEnumerable<JsonElement> For(ILevel level)
    {
        var identifiers = TimelinesRegistry.Identifiers;
        var name = LevelEnvironment.Timelines(level.DimensionType);
        if (name is null || !TagsRegistry.TryGet("timeline", name.TrimStart('#'), out var tag))
            yield break;

        foreach (var id in tag.Entries)
        {
            var identifier = identifiers[id];
            if (definitions.TryGetValue(identifier, out var definition))
                yield return definition;
        }
    }

    internal static bool Contains(ILevel level, string timeline)
    {
        var id = TimelinesRegistry.IdOf(timeline);
        var name = LevelEnvironment.Timelines(level.DimensionType);
        return name is not null
            && TagsRegistry.TryGet("timeline", name.TrimStart('#'), out var tag)
            && tag.Entries.Contains(id);
    }

    internal static bool Apply(ILevel level, string attribute, bool value)
    {
        foreach (var definition in For(level))
        {
            if (!definition.GetProperty("tracks").TryGetProperty(attribute, out var track))
                continue;

            var frames = Frames(level, definition, track);
            var sampled = frames.From.GetProperty("value").GetBoolean();
            var modifier = track.TryGetProperty("modifier", out var operation) ? operation.GetString() : "override";
            value = modifier switch
            {
                "or" => value || sampled,
                "and" => value && sampled,
                _ => sampled
            };
        }
        return value;
    }

    internal static float Apply(ILevel level, string attribute, float value)
    {
        foreach (var definition in For(level))
        {
            if (!definition.GetProperty("tracks").TryGetProperty(attribute, out var track))
                continue;

            var frames = Frames(level, definition, track);
            var from = frames.From.GetProperty("value").GetSingle();
            var to = frames.To.GetProperty("value").GetSingle();
            var sampled = from + frames.Progress * (to - from);
            var modifier = track.TryGetProperty("modifier", out var operation) ? operation.GetString() : "override";
            value = modifier switch
            {
                "multiply" => value * sampled,
                "maximum" => Math.Max(value, sampled),
                _ => sampled
            };
        }
        return value;
    }

    private readonly record struct SampleFrames(JsonElement From, JsonElement To, float Progress);

    private static SampleFrames Frames(ILevel level, JsonElement definition, JsonElement track)
    {
        var clock = definition.GetProperty("clock").GetString()!;
        var time = level.LevelData.WorldClocks.States[clock].TotalTicks;
        var frames = track.GetProperty("keyframes").EnumerateArray().ToArray();
        int? period = definition.TryGetProperty("period_ticks", out var ticks) ? ticks.GetInt32() : null;
        if (period is int length)
            time = (time % length + length) % length;

        var from = frames[^1];
        var fromTime = from.GetProperty("ticks").GetInt64() - (period ?? 0);
        foreach (var to in frames)
        {
            var toTime = to.GetProperty("ticks").GetInt64();
            if (time < toTime)
            {
                if (period is null && fromTime > time)
                    return new SampleFrames(frames[0], frames[0], 0);

                var progress = (float)(time - fromTime) / (toTime - fromTime);
                return new SampleFrames(from, to, progress);
            }
            from = to;
            fromTime = toTime;
        }

        if (period is not int repeat)
            return new SampleFrames(from, from, 0);

        var endTime = frames[0].GetProperty("ticks").GetInt64() + repeat;
        var endProgress = (float)(time - fromTime) / (endTime - fromTime);
        return new SampleFrames(from, frames[0], endProgress);
    }
}
