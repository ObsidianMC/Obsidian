using Obsidian.API.Registry.Codecs.Dimensions;
using System.Collections.Frozen;

namespace Obsidian.WorldData;

/// <summary>
/// What a level's dimension type decides about its environment, where vanilla 1.21.11 reads it from the type's
/// environment attributes and timelines rather than from which level it is. A plugin's level of the overworld's type
/// behaves like the overworld.
/// </summary>
internal static class LevelEnvironment
{
    // Vanilla's dimension types' timelines (data/minecraft/dimension_type/*.json), which the server's copy of vanilla's
    // types doesn't hold. Plugins' types have theirs in DimensionElement.Timelines.
    private static readonly FrozenDictionary<string, string> vanillaTimelines = new Dictionary<string, string>
    {
        ["minecraft:overworld"] = "#minecraft:in_overworld",
        ["minecraft:overworld_caves"] = "#minecraft:in_overworld",
        ["minecraft:the_nether"] = "#minecraft:in_nether",
        ["minecraft:the_end"] = "#minecraft:in_end"
    }.ToFrozenDictionary();

    // The keyframes of the minecraft:day timeline's monsters_burn track: true from 23460, false from 12542, over a
    // 24000 tick day. They decide when undead mobs start and stop burning, so they match vanilla's.
    private const int MonstersStopBurning = 12542;
    private const int MonstersStartBurning = 23460;

    /// <summary>
    /// Whether the level has vanilla's day cycle: its dimension type's timelines hold <c>minecraft:day</c>, which
    /// only vanilla's <c>#minecraft:in_overworld</c> tag does (plugins can only name vanilla's timeline tags).
    /// </summary>
    public static bool HasDayCycle(this ILevel level) =>
        Timelines(level.DimensionType) == "#minecraft:in_overworld";

    /// <summary>
    /// Whether undead mobs burn in daylight now: vanilla's <c>minecraft:gameplay/monsters_burn</c> attribute, set by
    /// the dimension type itself or, with the day cycle, by day. The caller checks the sky and the weather, as
    /// vanilla's <c>Mob.isSunBurnTick</c> does. Biomes' attributes aren't applied.
    /// </summary>
    public static bool MonstersBurn(this ILevel level)
    {
        if (level.DimensionType.Element.MonstersBurn)
            return true;

        if (!level.HasDayCycle())
            return false;

        var time = (level.DayTime % 24000 + 24000) % 24000;

        return time is < MonstersStopBurning or >= MonstersStartBurning;
    }

    private static string? Timelines(DimensionCodec type) =>
        type.Element.Timelines ?? vanillaTimelines.GetValueOrDefault(type.Name);
}
