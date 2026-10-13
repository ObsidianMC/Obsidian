using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Registries.Codecs;
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

    private const int DayLength = 24000;

    // Vanilla's types' own minecraft:gameplay/sky_light_level, where it isn't the default 15. Plugins' types have
    // theirs in DimensionElement.SkyLightLevel.
    private static readonly FrozenDictionary<string, float> vanillaSkyLightLevels = new Dictionary<string, float>
    {
        ["minecraft:the_nether"] = 4f
    }.ToFrozenDictionary();

    // The minecraft:day timeline's sky_light_level track, which multiplies the level by its value, eased linearly
    // between these keyframes over a looping day. With the weather's blends below, they decide the sky's darkening
    // and so where monsters spawn, so they match vanilla's (Timelines, WeatherAttributes).
    private static readonly (int Ticks, float Value)[] daySkyLight =
    [
        (133, 1f),
        (11867, 1f),
        (13670, 0.26666668f),
        (22330, 0.26666668f)
    ];

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/can_start_raid</c>: whether a bad omen starts raids.
    /// </summary>
    public const string CanStartRaid = "minecraft:gameplay/can_start_raid";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/can_pillager_patrol_spawn</c>: whether pillager patrols spawn.
    /// </summary>
    public const string CanPillagerPatrolSpawn = "minecraft:gameplay/can_pillager_patrol_spawn";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/fast_lava</c>: whether lava flows faster and further.
    /// </summary>
    public const string FastLava = "minecraft:gameplay/fast_lava";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/piglins_zombify</c>: whether piglins and hoglins turn into zombified ones.
    /// </summary>
    public const string PiglinsZombify = "minecraft:gameplay/piglins_zombify";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/snow_golem_melts</c>: whether snow golems take damage.
    /// </summary>
    public const string SnowGolemMelts = "minecraft:gameplay/snow_golem_melts";

    /// <summary>
    /// The boolean gameplay attributes the server applies for a dimension type, with vanilla's defaults
    /// (EnvironmentAttributes). Biomes' values of them aren't applied.
    /// </summary>
    public static readonly FrozenDictionary<string, bool> GameplayDefaults = new Dictionary<string, bool>
    {
        [CanStartRaid] = true,
        [CanPillagerPatrolSpawn] = true,
        [FastLava] = false,
        [PiglinsZombify] = true,
        [SnowGolemMelts] = false
    }.ToFrozenDictionary();

    // Vanilla's types' own values of those attributes, where they aren't the defaults (the nether's
    // data/minecraft/dimension_type/the_nether.json): the server's copy of vanilla's types doesn't hold attributes.
    private static readonly FrozenDictionary<string, FrozenDictionary<string, bool>> vanillaGameplay =
        new Dictionary<string, FrozenDictionary<string, bool>>
        {
            ["minecraft:the_nether"] = new Dictionary<string, bool>
            {
                [CanStartRaid] = false,
                [FastLava] = true,
                [PiglinsZombify] = false,
                [SnowGolemMelts] = true
            }.ToFrozenDictionary()
        }.ToFrozenDictionary();

    private const float WeatherSkyLightLevel = 4f;
    private const float RainSkyLightAlpha = 0.3125f;
    private const float ThunderSkyLightAlpha = 0.52734375f;

    /// <summary>
    /// Whether the level has vanilla's day cycle: its dimension type's timelines hold <c>minecraft:day</c>, which
    /// only vanilla's <c>#minecraft:in_overworld</c> tag does (plugins can only name vanilla's timeline tags).
    /// </summary>
    public static bool HasDayCycle(this ILevel level) =>
        Timelines(level.DimensionType) == "#minecraft:in_overworld";

    /// <summary>
    /// A boolean gameplay attribute of <see cref="GameplayDefaults"/> as the dimension type sets it: a plugin's or
    /// data pack's type's own value, vanilla's type's, or the attribute's default.
    /// </summary>
    /// <param name="attribute">The attribute, like <see cref="CanStartRaid"/>.</param>
    public static bool Gameplay(this DimensionCodec type, string attribute)
    {
        if (type.Element.GameplayAttributes?.TryGetValue(attribute, out var own) == true)
            return own;

        if (vanillaGameplay.TryGetValue(type.Name, out var vanilla) && vanilla.TryGetValue(attribute, out var value))
            return value;

        return GameplayDefaults[attribute];
    }

    /// <summary>
    /// Whether piglins and hoglins stay as they are: the type's <see cref="PiglinsZombify"/> is false, or code set
    /// its <see cref="DimensionElement.PiglinSafe"/>.
    /// </summary>
    public static bool PiglinsSafe(this DimensionCodec type) =>
        type.Element.PiglinSafe || !type.Gameplay(PiglinsZombify);

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

        var time = (level.DayTime % DayLength + DayLength) % DayLength;

        return time is < MonstersStopBurning or >= MonstersStartBurning;
    }

    /// <summary>
    /// The level's sky darkening, 0 to 15, as vanilla's <c>Level.updateSkyBrightness</c> computes it: 15 less its
    /// <c>minecraft:gameplay/sky_light_level</c>. That starts at 15, takes the dimension type's own value, then the day
    /// timeline's and, where weather can happen, rain's and thunder's. Biomes don't change it.
    /// </summary>
    public static int SkyDarken(this ILevel level)
    {
        var type = level.DimensionType;
        var light = type.Element.SkyLightLevel ?? vanillaSkyLightLevels.GetValueOrDefault(type.Name, 15f);

        if (level.HasDayCycle())
            light *= SampleDaySkyLight(level.DayTime);

        if (level.CanHaveWeather())
        {
            // Obsidian's weather starts and stops at once where vanilla's fades in and out, so its levels are 0 or 1.
            // Thunder's level is vanilla's thunder level times the rain's.
            var rain = level.LevelData.Raining ? 1f : 0f;
            var thunder = level.LevelData.Thundering ? rain : 0f;

            light = BlendWeather(light, rain - thunder, RainSkyLightAlpha);
            light = BlendWeather(light, thunder, ThunderSkyLightAlpha);
        }

        return (int)(15f - Math.Clamp(light, 0f, 15f));
    }

    /// <summary>
    /// Whether it's thundering in the level as vanilla's <c>Level.isThundering</c> decides: where weather can happen,
    /// while it rains and thunders.
    /// </summary>
    public static bool IsThundering(this ILevel level) =>
        level.CanHaveWeather()
        && level.LevelData.Raining
        && level.LevelData.Thundering;

    // Vanilla's Level.canHaveWeather: a sky without a ceiling, outside the end.
    private static bool CanHaveWeather(this ILevel level) =>
        level.DimensionType.Element.HasSkylight
        && !level.DimensionType.Element.HasCeiling
        && level.DimensionName != "minecraft:the_end";

    // Samples the day's sky light track as vanilla's KeyframeTrackSampler does: the first segment ending after the
    // time, the last keyframe wrapping round to the first a day later.
    private static float SampleDaySkyLight(int dayTime)
    {
        var time = (dayTime % DayLength + DayLength) % DayLength;
        var (fromTicks, fromValue) = (daySkyLight[^1].Ticks - DayLength, daySkyLight[^1].Value);

        foreach (var (toTicks, toValue) in daySkyLight.Append((daySkyLight[0].Ticks + DayLength, daySkyLight[0].Value)))
        {
            if (time < toTicks)
            {
                var progress = (float)(time - fromTicks) / (toTicks - fromTicks);

                return EnvironmentAttributeMap.Lerp(progress, fromValue, toValue);
            }

            (fromTicks, fromValue) = (toTicks, toValue);
        }

        return fromValue;
    }

    // A weather's layer: its blend towards the night's light, applied by how strong the weather is.
    private static float BlendWeather(float light, float strength, float alpha)
    {
        if (strength <= 0f)
            return light;

        var blended = EnvironmentAttributeMap.Lerp(alpha, light, WeatherSkyLightLevel);

        return EnvironmentAttributeMap.Lerp(strength, light, blended);
    }

    private static string? Timelines(DimensionCodec type) =>
        type.Element.Timelines ?? vanillaTimelines.GetValueOrDefault(type.Name);
}
