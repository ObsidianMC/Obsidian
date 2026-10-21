using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using Obsidian.Registries.Codecs;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// What a level's dimension type and biomes decide about its environment, where vanilla 1.21.11 reads it from their
/// environment attributes and timelines rather than from which level it is. A plugin's level of the overworld's type
/// behaves like the overworld.
/// </summary>
/// <remarks>
/// Like vanilla's environment attribute system, an attribute at a position starts at its default, then the dimension
/// type's map applies, then the biome's there. The maps of plugins' and data packs' types and biomes are attached to
/// their elements as their files are read (see <see cref="Attach(DimensionElement, EnvironmentAttributeMap)"/>);
/// vanilla's own entries' values, which the server's copies of them don't hold, are kept here.
/// </remarks>
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
    /// Vanilla's <c>minecraft:gameplay/increased_fire_burnout</c>: whether fire burns out faster there.
    /// </summary>
    public const string IncreasedFireBurnout = "minecraft:gameplay/increased_fire_burnout";
    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/piglins_zombify</c>: whether piglins and hoglins turn into zombified ones.
    /// </summary>
    public const string PiglinsZombify = "minecraft:gameplay/piglins_zombify";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/snow_golem_melts</c>: whether snow golems take damage.
    /// </summary>
    public const string SnowGolemMelts = "minecraft:gameplay/snow_golem_melts";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/respawn_anchor_works</c>: whether respawn anchors set spawn points rather than
    /// explode.
    /// </summary>
    public const string RespawnAnchorWorks = "minecraft:gameplay/respawn_anchor_works";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/water_evaporates</c>: whether water poured from a bucket evaporates.
    /// </summary>
    public const string WaterEvaporates = "minecraft:gameplay/water_evaporates";

    /// <summary>
    /// Vanilla's <c>minecraft:gameplay/bed_rule</c>: when beds let players sleep and set their spawn, and whether
    /// they explode.
    /// </summary>
    public const string BedRuleAttribute = "minecraft:gameplay/bed_rule";

    private const string MonstersBurnAttribute = "minecraft:gameplay/monsters_burn";

    /// <summary>
    /// The boolean gameplay attributes the server applies, with vanilla's defaults (EnvironmentAttributes). All but
    /// <see cref="FastLava"/> apply where they're asked about, with the biome's values there.
    /// </summary>
    public static readonly FrozenDictionary<string, bool> GameplayDefaults = new Dictionary<string, bool>
    {
        [CanStartRaid] = true,
        [CanPillagerPatrolSpawn] = true,
        [FastLava] = false,
        [IncreasedFireBurnout] = false,
        [PiglinsZombify] = true,
        [SnowGolemMelts] = false,
        [RespawnAnchorWorks] = false,
        [WaterEvaporates] = false
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
                [SnowGolemMelts] = true,
                [RespawnAnchorWorks] = true,
                [WaterEvaporates] = true
            }.ToFrozenDictionary()
        }.ToFrozenDictionary();

    // Vanilla's biomes' own values of the attributes the server applies (data/minecraft/worldgen/biome/*.json): hot
    // biomes melt snow golems, and patrols don't spawn in mushroom fields.
    private static readonly FrozenDictionary<string, FrozenDictionary<string, bool>> vanillaBiomeGameplay =
        new Dictionary<string, FrozenDictionary<string, bool>>
        {
            ["minecraft:badlands"] = Melts(),
            ["minecraft:desert"] = Melts(),
            ["minecraft:eroded_badlands"] = Melts(),
            ["minecraft:savanna"] = Melts(),
            ["minecraft:savanna_plateau"] = Melts(),
            ["minecraft:windswept_savanna"] = Melts(),
            ["minecraft:wooded_badlands"] = Melts(),
            ["minecraft:mushroom_fields"] = new Dictionary<string, bool>
            {
                [CanPillagerPatrolSpawn] = false
            }.ToFrozenDictionary()
        }.ToFrozenDictionary();

    // Vanilla's types whose has_fixed_time is true (data/minecraft/dimension_type/*.json).
    private static readonly FrozenSet<string> vanillaFixedTime =
        new[] { "minecraft:the_nether", "minecraft:the_end" }.ToFrozenSet();

    // Vanilla's types' bed rules: the overworld's is the default, and beds explode in the nether and the end.
    private static readonly FrozenDictionary<string, BedRule> vanillaBedRules = new Dictionary<string, BedRule>
    {
        ["minecraft:the_nether"] = BedRule.Explodes,
        ["minecraft:the_end"] = BedRule.Explodes
    }.ToFrozenDictionary();

    // The attribute maps of plugins' and data packs' dimension types and biomes, by their elements.
    private static readonly ConditionalWeakTable<object, EnvironmentAttributeMap> attributeMaps = new();

    /// <summary>
    /// Attaches a dimension type's attribute map, read from its file, to its element.
    /// </summary>
    public static void Attach(DimensionElement element, EnvironmentAttributeMap attributes) =>
        attributeMaps.AddOrUpdate(element, attributes);

    /// <summary>
    /// Attaches a biome's attribute map, read from its file, to its element.
    /// </summary>
    public static void Attach(BiomeElement element, EnvironmentAttributeMap attributes) =>
        attributeMaps.AddOrUpdate(element, attributes);

    /// <summary>
    /// A boolean gameplay attribute of <see cref="GameplayDefaults"/> at <paramref name="position"/>: the dimension
    /// type's value (see <see cref="Gameplay(DimensionCodec, string)"/>) with the biome's there applied, vanilla's or
    /// a plugin's, like vanilla's <c>environmentAttributes().getValue(attribute, pos)</c>.
    /// </summary>
    public static bool Gameplay(this ILevel level, string attribute, Vector position)
    {
        var value = level.DimensionType.Gameplay(attribute);

        return BiomeAt(level, position) is BiomeCodec biome ? ApplyBiome(biome, attribute, value) : value;
    }

    /// <summary>
    /// The bed rule at <paramref name="position"/>: vanilla's default (sleep when it's dark, always set the spawn),
    /// then the dimension type's, then the biome's there.
    /// </summary>
    public static BedRule BedRuleAt(this ILevel level, Vector position)
    {
        var type = level.DimensionType;
        var rule = attributeMaps.TryGetValue(type.Element, out var typeMap)
            ? BedRule.From(typeMap.Find(BedRuleAttribute)) ?? BedRule.Default
            : vanillaBedRules.GetValueOrDefault(type.Name, BedRule.Default);

        if (BiomeAt(level, position) is BiomeCodec biome && attributeMaps.TryGetValue(biome.Element, out var biomeMap))
            rule = BedRule.From(biomeMap.Find(BedRuleAttribute)) ?? rule;

        return rule;
    }

    /// <summary>
    /// Whether it's dark outside, which beds with a <c>when_dark</c> rule need (vanilla's <c>Level.isDarkOutside</c>):
    /// the dimension type's time isn't fixed and the sky is darkened by 4 or more.
    /// </summary>
    public static bool IsDarkOutside(this ILevel level) => !level.HasFixedTime() && level.SkyDarken() >= 4;

    /// <summary>
    /// Whether the dimension type's time is fixed (its <c>has_fixed_time</c>): a plugin's type's own flag, or
    /// vanilla's, which only the nether's and the end's set.
    /// </summary>
    public static bool HasFixedTime(this ILevel level)
    {
        var type = level.DimensionType;

        return type.Element.HasFixedTime ?? vanillaFixedTime.Contains(type.Name);
    }

    private static bool ApplyBiome(BiomeCodec biome, string attribute, bool value)
    {
        if (attributeMaps.TryGetValue(biome.Element, out var map))
            return map.Apply(attribute, value);

        return vanillaBiomeGameplay.TryGetValue(biome.Name, out var own) && own.TryGetValue(attribute, out var set)
            ? set
            : value;
    }

    // The biome at a loaded position, which the attributes there come from.
    private static BiomeCodec? BiomeAt(ILevel level, Vector position)
    {
        if (level is not AbstractLevel concrete || concrete.IsOutsideBuildHeight(position.Y))
            return null;

        return concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetBiome(position);
    }

    private static FrozenDictionary<string, bool> Melts() =>
        new Dictionary<string, bool> { [SnowGolemMelts] = true }.ToFrozenDictionary();

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
    /// Whether piglins and hoglins stay as they are anywhere in the dimension type: the type's
    /// <see cref="PiglinsZombify"/> is false, or code set its <see cref="DimensionElement.PiglinSafe"/>.
    /// </summary>
    public static bool PiglinsSafe(this DimensionCodec type) =>
        type.Element.PiglinSafe || !type.Gameplay(PiglinsZombify);

    /// <summary>
    /// Whether piglins and hoglins stay as they are at <paramref name="position"/>: <see cref="PiglinsZombify"/> is
    /// false there, or code set the type's <see cref="DimensionElement.PiglinSafe"/>.
    /// </summary>
    public static bool PiglinsSafe(this ILevel level, Vector position) =>
        level.DimensionType.Element.PiglinSafe || !level.Gameplay(PiglinsZombify, position);

    /// <summary>
    /// Whether undead mobs burn in daylight now at <paramref name="position"/>: vanilla's
    /// <c>minecraft:gameplay/monsters_burn</c> attribute, set by the dimension type itself, then by the biome there,
    /// then, with the day cycle, by day. The caller checks the sky and the weather, as vanilla's
    /// <c>Mob.isSunBurnTick</c> does.
    /// </summary>
    /// <remarks>
    /// The order is vanilla's environment attribute layers' (dimension, biome, timelines), which decides that a biome
    /// can't stop monsters burning by day: the day timeline's <c>or</c> applies after it.
    /// </remarks>
    public static bool MonstersBurn(this ILevel level, Vector position)
    {
        var burn = level.DimensionType.Element.MonstersBurn;

        // Vanilla's biomes don't set it.
        if (BiomeAt(level, position) is BiomeCodec biome && attributeMaps.TryGetValue(biome.Element, out var map))
            burn = map.Apply(MonstersBurnAttribute, burn);

        if (!burn && level.HasDayCycle())
        {
            var time = (level.DayTime % DayLength + DayLength) % DayLength;
            burn = time is < MonstersStopBurning or >= MonstersStartBurning;
        }

        return burn;
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

/// <summary>
/// Vanilla's <c>BedRule</c>: when a bed lets players sleep and set their spawn (<c>always</c>, <c>when_dark</c> or
/// <c>never</c>), whether it explodes instead, and what players are told when it doesn't let them sleep.
/// </summary>
internal sealed record BedRule(string CanSleep, string CanSetSpawn, bool Explode, ChatMessage? ErrorMessage)
{
    /// <summary>
    /// Vanilla's default and the overworld's: sleep when it's dark, always set the spawn.
    /// </summary>
    public static BedRule Default { get; } = new(
        "when_dark",
        "always",
        false,
        new ChatMessage { Translate = "block.minecraft.bed.no_sleep" });

    /// <summary>
    /// The nether's and the end's: beds explode.
    /// </summary>
    public static BedRule Explodes { get; } = new("never", "never", true, null);

    /// <summary>
    /// Whether the rule lets a player sleep in <paramref name="level"/> now.
    /// </summary>
    public bool AllowsSleep(ILevel level) => this.CanSleep switch
    {
        "always" => true,
        "when_dark" => level.IsDarkOutside(),
        _ => false
    };

    /// <summary>
    /// The rule an attribute map's entry holds, as <see cref="EnvironmentAttributes"/> reads it; null without one.
    /// </summary>
    public static BedRule? From(INbtTag? entry)
    {
        if (entry is not NbtCompound rule
            || !rule.TryGetTag<NbtTag<string>>("can_sleep", out var canSleep)
            || !rule.TryGetTag<NbtTag<string>>("can_set_spawn", out var canSetSpawn))
        {
            return null;
        }

        var explodes = rule.TryGetTag<NbtTag<byte>>("explodes", out var flag) && flag.Value != 0;
        var message = rule.TryGetTag<NbtTag<string>>("error_message", out var json) ? Message(json.Value) : null;

        return new BedRule(canSleep.Value, canSetSpawn.Value, explodes, message);
    }

    // A text component's JSON, as far as ChatMessage models it: a string, an object, or a list of them, the rest of
    // which are extras of the first.
    private static ChatMessage? Message(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return Message(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ChatMessage? Message(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return ChatMessage.Simple(value.GetString()!);
            case JsonValueKind.Array when value.GetArrayLength() > 0 && Message(value[0]) is ChatMessage first:
                return first.AddExtra(Messages(value.EnumerateArray().Skip(1)));
            case JsonValueKind.Object:
                break;
            default:
                return null;
        }

        // Its text or translation and its style flags; colours and events aren't shown.
        var message = new ChatMessage
        {
            Text = String("text"),
            Translate = String("translate"),
            Bold = Flag("bold"),
            Italic = Flag("italic"),
            Underlined = Flag("underlined"),
            Strikethrough = Flag("strikethrough"),
            Obfuscated = Flag("obfuscated")
        };

        if (value.TryGetProperty("with", out var with) && with.ValueKind == JsonValueKind.Array)
            message.AddChatComponent(Messages(with.EnumerateArray()));

        if (value.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
            message.AddExtra(Messages(extra.EnumerateArray()));

        return message;

        string? String(string name) =>
            value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
                ? field.GetString()
                : null;

        bool? Flag(string name) =>
            value.TryGetProperty(name, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? field.GetBoolean()
                : null;
    }

    // A translation's arguments may be numbers and booleans too, which vanilla shows as text.
    private static List<ChatMessage> Messages(IEnumerable<JsonElement> values) =>
    [
        .. values.Select(value => value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? ChatMessage.Simple(value.GetRawText())
            : Message(value))
            .OfType<ChatMessage>()
    ];
}
