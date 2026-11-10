using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
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

    // The timelines' tracks, read once from their JSON. Random ticks and mobs sample them many times a tick (a grass
    // block's tick reads the sky light), and reading the JSON on each sample cost more than the ticks themselves.
    private static readonly FrozenDictionary<string, Timeline> timelines =
        definitions.ToFrozenDictionary(entry => entry.Key, entry => Timeline.Read(entry.Value));

    // Timeline tag names without their '#', so that sampling doesn't trim one each time.
    private static readonly ConcurrentDictionary<string, string> tagIdentifiers = new();

    private sealed record Timeline(string Clock, int? Period, FrozenDictionary<string, Track> Tracks)
    {
        public static Timeline Read(JsonElement definition)
        {
            var clock = definition.GetProperty("clock").GetString()!;
            int? period = definition.TryGetProperty("period_ticks", out var ticks) ? ticks.GetInt32() : null;

            var tracks = new Dictionary<string, Track>();
            if (definition.TryGetProperty("tracks", out var values))
            {
                foreach (var entry in values.EnumerateObject())
                    tracks.Add(entry.Name, Track.Read(entry.Value));
            }

            return new Timeline(clock, period, tracks.ToFrozenDictionary());
        }
    }

    // Constant is whether the track's ease is vanilla's EasingType.CONSTANT, which holds each keyframe's value until
    // the next rather than blending towards it. Other eases are sampled linearly.
    private sealed record Track(string Modifier, Keyframe[] Keyframes, bool Constant)
    {
        public static Track Read(JsonElement track)
        {
            var modifier = track.TryGetProperty("modifier", out var operation) ? operation.GetString()! : "override";
            var keyframes = track.GetProperty("keyframes")
                .EnumerateArray()
                .Select(Keyframe.Read)
                .ToArray();
            var constant = track.TryGetProperty("ease", out var ease)
                && ease.ValueKind == JsonValueKind.String
                && ease.GetString() == "constant";

            return new Track(modifier, keyframes, constant);
        }
    }

    // A keyframe's value as the attribute reads it: a flag for a boolean attribute, a number for a float one. Other
    // kinds of value (strings) are never sampled.
    private readonly record struct Keyframe(long Ticks, bool Flag, float Number)
    {
        public static Keyframe Read(JsonElement keyframe)
        {
            var value = keyframe.GetProperty("value");
            var flag = value.ValueKind == JsonValueKind.True;
            var number = value.ValueKind == JsonValueKind.Number ? value.GetSingle() : 0f;

            return new Keyframe(keyframe.GetProperty("ticks").GetInt64(), flag, number);
        }
    }

    // The tag that lists the timelines of the level's dimension type, in the order they apply.
    private static bool TryGetTag(ILevel level, [NotNullWhen(true)] out Tag? tag)
    {
        tag = null;

        var name = LevelEnvironment.Timelines(level.DimensionType);
        if (name is null)
            return false;

        var identifier = tagIdentifiers.GetOrAdd(name, static name => name.TrimStart('#'));
        return TagsRegistry.TryGet("timeline", identifier, out tag);
    }

    internal static bool Contains(ILevel level, string timeline)
    {
        var id = TimelinesRegistry.IdOf(timeline);
        return TryGetTag(level, out var tag) && tag.Entries.Contains(id);
    }

    internal static bool Apply(ILevel level, string attribute, bool value)
    {
        if (!TryGetTag(level, out var tag))
            return value;

        var identifiers = TimelinesRegistry.Identifiers;
        foreach (var id in tag.Entries)
        {
            if (!timelines.TryGetValue(identifiers[id], out var timeline))
                continue;

            if (!timeline.Tracks.TryGetValue(attribute, out var track))
                continue;

            var frames = Frames(level, timeline, track);
            var sampled = frames.From.Flag;
            value = track.Modifier switch
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
        if (!TryGetTag(level, out var tag))
            return value;

        var identifiers = TimelinesRegistry.Identifiers;
        foreach (var id in tag.Entries)
        {
            if (!timelines.TryGetValue(identifiers[id], out var timeline))
                continue;

            if (!timeline.Tracks.TryGetValue(attribute, out var track))
                continue;

            var frames = Frames(level, timeline, track);
            var from = frames.From.Number;
            var to = frames.To.Number;
            var progress = track.Constant ? 0f : frames.Progress;
            var sampled = from + progress * (to - from);
            value = track.Modifier switch
            {
                "multiply" => value * sampled,
                "maximum" => Math.Max(value, sampled),
                _ => sampled
            };
        }

        return value;
    }

    private readonly record struct SampleFrames(Keyframe From, Keyframe To, float Progress);

    private static SampleFrames Frames(ILevel level, Timeline timeline, Track track)
    {
        var time = level.LevelData.WorldClocks.States[timeline.Clock].TotalTicks;
        var frames = track.Keyframes;
        var period = timeline.Period;
        if (period is int length)
            time = (time % length + length) % length;

        var from = frames[^1];
        var fromTime = from.Ticks - (period ?? 0);
        foreach (var to in frames)
        {
            var toTime = to.Ticks;
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

        var endTime = frames[0].Ticks + repeat;
        var endProgress = (float)(time - fromTime) / (endTime - fromTime);
        return new SampleFrames(from, frames[0], endProgress);
    }
}
