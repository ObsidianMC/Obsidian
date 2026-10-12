using Obsidian.Nbt;
using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// An environment attribute map, the <c>attributes</c> of a 1.21.11 biome or dimension type: values by attribute,
/// each given as is (vanilla's override) or as <c>{"modifier": ..., "argument": ...}</c>.
/// </summary>
/// <param name="Entries">Each attribute's entry, encoded as vanilla's codecs write it.</param>
internal sealed record EnvironmentAttributeMap(ImmutableArray<EnvironmentAttributeEntry> Entries)
{
    public static EnvironmentAttributeMap Empty { get; } = new([]);

    /// <summary>
    /// A boolean attribute's value once this map applies to <paramref name="value"/>: its override, or its modifier
    /// with vanilla's BooleanModifier operations. Without an entry the value stays.
    /// </summary>
    /// <param name="attribute">The attribute's identifier, like <c>minecraft:gameplay/monsters_burn</c>.</param>
    public bool Apply(string attribute, bool value)
    {
        var entry = this.Entries.FirstOrDefault(entry => entry.Attribute == attribute);

        switch (entry?.Value)
        {
            case NbtTag<byte> overridden:
                return overridden.Value != 0;
            case NbtCompound modifier
                when modifier.TryGetTag<NbtTag<string>>("modifier", out var operation)
                && modifier.TryGetTag<NbtTag<byte>>("argument", out var argumentTag):
                var argument = argumentTag.Value != 0;

                return operation.Value switch
                {
                    "and" => value && argument,
                    "nand" => !(value && argument),
                    "or" => value || argument,
                    "nor" => !(value || argument),
                    "xor" => value ^ argument,
                    "xnor" => value == argument,
                    _ => value
                };
            default:
                return value;
        }
    }

    /// <summary>
    /// The map as vanilla's EnvironmentAttributeMap.NETWORK_CODEC writes it: only the attributes clients sync. A
    /// map that isn't empty is written even if none of its attributes are synced, as vanilla writes it.
    /// </summary>
    public NbtCompound ToNetworkNbt(string name)
    {
        var compound = new NbtCompound(name);
        foreach (var entry in this.Entries.Where(entry => entry.Syncable))
            compound.Add(entry.Value);

        return compound;
    }
}

/// <param name="Attribute">The attribute's identifier, like <c>minecraft:visual/sky_color</c>.</param>
/// <param name="Syncable">Whether clients get the attribute.</param>
/// <param name="Value">The value or modifier, named after the attribute.</param>
internal sealed record EnvironmentAttributeEntry(string Attribute, bool Syncable, INbtTag Value);

/// <summary>
/// Reads environment attribute maps like vanilla 1.21.11's EnvironmentAttributeMap codecs, with the value codec,
/// range and modifiers of each of vanilla's attributes (EnvironmentAttributes, AttributeTypes, AttributeModifier).
/// </summary>
/// <remarks>
/// Two kinds of value can't be read yet, and a file that sets one is rejected: particles that take options (like
/// <c>minecraft:dust</c>), and a bed rule's <c>error_message</c> beyond plain or translated text.
/// </remarks>
internal static class EnvironmentAttributes
{
    private const string Override = "override";

    private enum ValueType
    {
        Boolean,
        TriState,
        Float,
        RgbColor,
        ArgbColor,
        MoonPhase,
        Activity,
        BedRule,
        Particle,
        AmbientParticles,
        BackgroundMusic,
        AmbientSounds
    }

    // Whether clients get an attribute (vanilla's syncable), whether biomes may set it (isPositional) and its range.
    private sealed record Definition(
        ValueType Type,
        bool Syncable,
        bool Positional = true,
        float Min = float.NegativeInfinity,
        float Max = float.PositiveInfinity);

    // Vanilla 1.21.11's attributes. The ids, types, flags and ranges have to match vanilla's: they decide what files
    // vanilla accepts and what clients are sent.
    private static readonly FrozenDictionary<string, Definition> definitions = new Dictionary<string, Definition>
    {
        ["visual/fog_color"] = new(ValueType.RgbColor, Syncable: true),
        ["visual/fog_start_distance"] = new(ValueType.Float, Syncable: true),
        ["visual/fog_end_distance"] = new(ValueType.Float, Syncable: true, Min: 0),
        ["visual/sky_fog_end_distance"] = new(ValueType.Float, Syncable: true, Min: 0),
        ["visual/cloud_fog_end_distance"] = new(ValueType.Float, Syncable: true, Min: 0),
        ["visual/water_fog_color"] = new(ValueType.RgbColor, Syncable: true),
        ["visual/water_fog_start_distance"] = new(ValueType.Float, Syncable: true),
        ["visual/water_fog_end_distance"] = new(ValueType.Float, Syncable: true, Min: 0),
        ["visual/sky_color"] = new(ValueType.RgbColor, Syncable: true),
        ["visual/sunrise_sunset_color"] = new(ValueType.ArgbColor, Syncable: true),
        ["visual/cloud_color"] = new(ValueType.ArgbColor, Syncable: true),
        ["visual/cloud_height"] = new(ValueType.Float, Syncable: true),
        ["visual/sun_angle"] = new(ValueType.Float, Syncable: true),
        ["visual/moon_angle"] = new(ValueType.Float, Syncable: true),
        ["visual/star_angle"] = new(ValueType.Float, Syncable: true),
        ["visual/moon_phase"] = new(ValueType.MoonPhase, Syncable: true),
        ["visual/star_brightness"] = new(ValueType.Float, Syncable: true, Min: 0, Max: 1),
        ["visual/sky_light_color"] = new(ValueType.RgbColor, Syncable: true),
        ["visual/sky_light_factor"] = new(ValueType.Float, Syncable: true, Min: 0, Max: 1),
        ["visual/default_dripstone_particle"] = new(ValueType.Particle, Syncable: true),
        ["visual/ambient_particles"] = new(ValueType.AmbientParticles, Syncable: true),
        ["audio/background_music"] = new(ValueType.BackgroundMusic, Syncable: true),
        ["audio/music_volume"] = new(ValueType.Float, Syncable: true, Min: 0, Max: 1),
        ["audio/ambient_sounds"] = new(ValueType.AmbientSounds, Syncable: true),
        ["audio/firefly_bush_sounds"] = new(ValueType.Boolean, Syncable: true),
        ["gameplay/sky_light_level"] = new(ValueType.Float, Syncable: true, Positional: false, Min: 0, Max: 15),
        ["gameplay/can_start_raid"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/water_evaporates"] = new(ValueType.Boolean, Syncable: true),
        ["gameplay/bed_rule"] = new(ValueType.BedRule, Syncable: false),
        ["gameplay/respawn_anchor_works"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/nether_portal_spawns_piglin"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/fast_lava"] = new(ValueType.Boolean, Syncable: true, Positional: false),
        ["gameplay/increased_fire_burnout"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/eyeblossom_open"] = new(ValueType.TriState, Syncable: false),
        ["gameplay/turtle_egg_hatch_chance"] = new(ValueType.Float, Syncable: false, Min: 0, Max: 1),
        ["gameplay/piglins_zombify"] = new(ValueType.Boolean, Syncable: true),
        ["gameplay/snow_golem_melts"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/creaking_active"] = new(ValueType.Boolean, Syncable: true),
        ["gameplay/surface_slime_spawn_chance"] = new(ValueType.Float, Syncable: false, Min: 0, Max: 1),
        ["gameplay/cat_waking_up_gift_chance"] = new(ValueType.Float, Syncable: false, Min: 0, Max: 1),
        ["gameplay/bees_stay_in_hive"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/monsters_burn"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/can_pillager_patrol_spawn"] = new(ValueType.Boolean, Syncable: false),
        ["gameplay/villager_activity"] = new(ValueType.Activity, Syncable: false),
        ["gameplay/baby_villager_activity"] = new(ValueType.Activity, Syncable: false)
    }.ToFrozenDictionary(entry => $"minecraft:{entry.Key}", entry => entry.Value);

    private static readonly string[] moonPhases =
    [
        "full_moon", "waning_gibbous", "third_quarter", "waning_crescent", "new_moon", "waxing_crescent",
        "first_quarter", "waxing_gibbous"
    ];

    private static readonly string[] bedRules = ["always", "when_dark", "never"];

    private static readonly string[] musicFields = ["default", "creative", "underwater"];

    /// <summary>
    /// Reads a map like vanilla's EnvironmentAttributeMap.CODEC, or CODEC_ONLY_POSITIONAL for a biome, which can't
    /// set attributes that apply to a whole dimension.
    /// </summary>
    public static EnvironmentAttributeMap Read(JsonElement value, string path, bool onlyPositional)
    {
        Object(value, path);

        var entries = ImmutableArray.CreateBuilder<EnvironmentAttributeEntry>();
        var seen = new HashSet<string>();

        // A repeated key keeps its last value, like Gson.
        var properties = value.EnumerateObject()
            .GroupBy(property => property.Name)
            .Select(group => group.Last());

        foreach (var property in properties)
        {
            var entryPath = Field(path, property.Name);
            if (!TryParseIdentifier(property.Name, out var id) || !definitions.TryGetValue(id, out var definition))
                throw Error(entryPath, "isn't one of vanilla's environment attributes");

            // Like "minecraft:visual/sky_color" next to "visual/sky_color".
            if (!seen.Add(id))
                throw Error(entryPath, $"{id} is given twice");

            if (onlyPositional && !definition.Positional)
                throw Error(entryPath, "applies to a whole dimension, so a biome can't set it");

            var entry = ReadEntry(definition, property.Value, entryPath, id);
            entries.Add(new EnvironmentAttributeEntry(id, definition.Syncable, entry));
        }

        return new EnvironmentAttributeMap(entries.ToImmutable());
    }

    // Like vanilla's EnvironmentAttributeMap.Entry codec: the value as is if it reads as one (an override), or else a
    // modifier of the attribute's type and its argument.
    private static INbtTag ReadEntry(Definition definition, JsonElement value, string path, string name)
    {
        InvalidDataException asValue;
        try
        {
            return ReadValue(definition, value, path, name);
        }
        catch (InvalidDataException ex)
        {
            asValue = ex;
        }

        if (value.ValueKind != JsonValueKind.Object || !TryGet(value, "modifier", out var modifierValue))
            throw asValue;

        var modifier = String(modifierValue, Field(path, "modifier"));
        var argumentPath = Field(path, "argument");
        var argument = Required(value, path, "argument");

        // An override is written as its value.
        if (modifier == Override)
            return ReadValue(definition, argument, argumentPath, name);

        var encoded = ReadArgument(definition.Type, modifier, argument, argumentPath)
            ?? throw Error(Field(path, "modifier"), $"\"{modifier}\" isn't a modifier of this attribute");

        return new NbtCompound(name)
        {
            new NbtTag<string>("modifier", modifier),
            encoded
        };
    }

    // A value of the attribute's type, checked against its range.
    private static INbtTag ReadValue(Definition definition, JsonElement value, string path, string name) =>
        definition.Type switch
        {
            ValueType.Boolean => Byte(name, Bool(value, path)),
            ValueType.TriState => ReadTriState(value, path, name),
            ValueType.Float => new NbtTag<float>(name, Float(value, path, definition.Min, definition.Max)),
            ValueType.RgbColor => new NbtTag<string>(name, FormatRgb(RgbColor(value, path))),
            ValueType.ArgbColor => new NbtTag<string>(name, FormatArgb(ArgbColor(value, path))),
            ValueType.MoonPhase => new NbtTag<string>(name, Enum(value, path, moonPhases)),
            ValueType.Activity => new NbtTag<string>(name, ReadActivity(value, path)),
            ValueType.BedRule => ReadBedRule(value, path, name),
            ValueType.Particle => ReadParticle(value, path, name),
            ValueType.AmbientParticles => ReadAmbientParticles(value, path, name),
            ValueType.BackgroundMusic => ReadBackgroundMusic(value, path, name),
            ValueType.AmbientSounds => ReadAmbientSounds(value, path, name),
            _ => throw new ArgumentOutOfRangeException(nameof(definition))
        };

    // The argument of a modifier from the type's library (vanilla's AttributeModifier libraries), named "argument";
    // null if the type has no such modifier. Unlike a value, an argument has no range.
    private static INbtTag? ReadArgument(ValueType type, string modifier, JsonElement value, string path)
    {
        const string name = "argument";

        return (type, modifier) switch
        {
            (ValueType.Boolean, "and" or "nand" or "or" or "nor" or "xor" or "xnor") =>
                Byte(name, Bool(value, path)),
            (ValueType.Float, "add" or "subtract" or "multiply" or "minimum" or "maximum") =>
                new NbtTag<float>(name, Float(value, path)),
            (ValueType.Float, "alpha_blend") => ReadFloatWithAlpha(value, path),
            (ValueType.RgbColor or ValueType.ArgbColor, "alpha_blend") =>
                new NbtTag<string>(name, FormatArgb(ArgbColor(value, path))),
            (ValueType.RgbColor, "add" or "subtract" or "multiply") or (ValueType.ArgbColor, "add" or "subtract") =>
                new NbtTag<string>(name, FormatRgb(RgbColor(value, path))),
            (ValueType.ArgbColor, "multiply") => ReadArgbMultiplier(value, path),
            (ValueType.RgbColor or ValueType.ArgbColor, "blend_to_gray") => ReadBlendToGray(value, path),
            _ => null
        };
    }

    private static NbtTag<byte> Byte(string name, bool value) => new(name, value ? (byte)1 : (byte)0);

    // Vanilla's TriState: true or false, as a boolean or its name, or "default". Booleans are written as booleans.
    private static INbtTag ReadTriState(JsonElement value, string path, string name)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return Byte(name, Bool(value, path));

        var state = Enum(value, path, "true", "false", "default");

        return state == "default" ? new NbtTag<string>(name, state) : Byte(name, state == "true");
    }

    private static string ReadActivity(JsonElement value, string path)
    {
        var activity = Identifier(value, path);

        return RegistryIds.IsActivity(activity) ? activity : throw Error(path, $"{activity} isn't an activity");
    }

    // Vanilla's FloatWithAlpha: a float, or {"value": float, "alpha": 0 to 1, default 1}, written as a float when
    // its alpha is 1.
    private static INbtTag ReadFloatWithAlpha(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return new NbtTag<float>("argument", Float(value, path));

        Object(value, path);

        var number = Float(Required(value, path, "value"), Field(path, "value"));
        var alpha = TryGet(value, "alpha", out var alphaValue) ? Float(alphaValue, Field(path, "alpha"), 0, 1) : 1f;

        return alpha == 1f
            ? new NbtTag<float>("argument", number)
            : new NbtCompound("argument")
            {
                new NbtTag<float>("value", number),
                new NbtTag<float>("alpha", alpha)
            };
    }

    // Vanilla's ARGB multiply argument: an ARGB colour or an RGB one, written as an int when opaque and as
    // "#aarrggbb" otherwise.
    private static INbtTag ReadArgbMultiplier(JsonElement value, string path)
    {
        int color;
        try
        {
            color = ArgbColor(value, path);
        }
        catch (InvalidDataException)
        {
            // Only a list of 3 floats gets here, an opaque colour.
            color = RgbIntColor(value, path);
        }

        return (uint)color >> 24 == 0xFF
            ? new NbtTag<int>("argument", color)
            : new NbtTag<string>("argument", FormatArgb(color));
    }

    private static NbtCompound ReadBlendToGray(JsonElement value, string path)
    {
        Object(value, path);

        var brightness = Float(Required(value, path, "brightness"), Field(path, "brightness"), 0, 1);
        var factor = Float(Required(value, path, "factor"), Field(path, "factor"), 0, 1);

        return new NbtCompound("argument")
        {
            new NbtTag<float>("brightness", brightness),
            new NbtTag<float>("factor", factor)
        };
    }

    // Vanilla's BedRule. Clients aren't sent bed rules, so its error message is checked but not kept.
    private static NbtCompound ReadBedRule(JsonElement value, string path, string name)
    {
        Object(value, path);

        if (TryGet(value, "error_message", out var message))
            CheckPlainText(message, Field(path, "error_message"));

        var canSleep = Enum(Required(value, path, "can_sleep"), Field(path, "can_sleep"), bedRules);
        var canSetSpawn = Enum(Required(value, path, "can_set_spawn"), Field(path, "can_set_spawn"), bedRules);

        var rule = new NbtCompound(name)
        {
            new NbtTag<string>("can_sleep", canSleep),
            new NbtTag<string>("can_set_spawn", canSetSpawn)
        };

        if (TryGet(value, "explodes", out var explodes) && Bool(explodes, Field(path, "explodes")))
            rule.Add(Byte("explodes", true));

        return rule;
    }

    // The text components vanilla's ComponentSerialization.CODEC reads that are only text: a string, {"text": ...},
    // or {"translate": ..., "fallback": ...}. Vanilla takes richer components too, which can't be read yet.
    private static void CheckPlainText(JsonElement value, string path)
    {
        const string expected = "plugins can only give a string, {\"text\": ...} or {\"translate\": ...} here yet";

        if (value.ValueKind == JsonValueKind.String)
            return;

        if (value.ValueKind != JsonValueKind.Object)
            throw Error(path, expected);

        var names = value.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet();

        if (names.SetEquals(["text"]))
        {
            String(value.GetProperty("text"), Field(path, "text"));
            return;
        }

        if (!names.Contains("translate") || !names.IsSubsetOf(["translate", "fallback"]))
            throw Error(path, expected);

        String(value.GetProperty("translate"), Field(path, "translate"));

        if (value.TryGetProperty("fallback", out var fallback))
            String(fallback, Field(path, "fallback"));
    }

    // Vanilla's ParticleTypes.CODEC: {"type": particle type, ...its options}. Only types without options are read.
    private static NbtCompound ReadParticle(JsonElement value, string path, string name)
    {
        Object(value, path);

        var typePath = Field(path, "type");
        var type = Identifier(Required(value, path, "type"), typePath);

        if (!RegistryIds.IsParticleType(type))
            throw Error(typePath, $"{type} isn't a particle type");

        if (RegistryIds.HasOptions(type))
            throw Error(typePath, $"{type} takes options, which plugins can't set yet");

        return new NbtCompound(name)
        {
            new NbtTag<string>("type", type)
        };
    }

    // A list of {"particle": particle, "probability": 0 to 1}.
    private static NbtList ReadAmbientParticles(JsonElement value, string path, string name)
    {
        Array(value, path);

        var list = new NbtList(NbtTagType.Compound, name);
        var index = 0;

        foreach (var item in value.EnumerateArray())
        {
            var itemPath = Index(path, index++);
            Object(item, itemPath);

            var particle = ReadParticle(Required(item, itemPath, "particle"), Field(itemPath, "particle"), "particle");
            var probability = Float(Required(item, itemPath, "probability"), Field(itemPath, "probability"), 0, 1);

            list.Add(new NbtCompound
            {
                particle,
                new NbtTag<float>("probability", probability)
            });
        }

        return list;
    }

    // Vanilla's BackgroundMusic: optional default, creative and underwater music.
    private static NbtCompound ReadBackgroundMusic(JsonElement value, string path, string name)
    {
        Object(value, path);

        var music = new NbtCompound(name);
        foreach (var field in musicFields)
        {
            if (TryGet(value, field, out var item))
                music.Add(ReadMusic(item, Field(path, field), field));
        }

        return music;
    }

    // Vanilla's Music: a sound, delays that aren't negative, and whether it replaces the current music (left out
    // when false).
    private static NbtCompound ReadMusic(JsonElement value, string path, string name)
    {
        Object(value, path);

        var minDelay = Int(Required(value, path, "min_delay"), Field(path, "min_delay"), 0, int.MaxValue);
        var maxDelay = Int(Required(value, path, "max_delay"), Field(path, "max_delay"), 0, int.MaxValue);

        var music = new NbtCompound(name)
        {
            ReadSound(Required(value, path, "sound"), Field(path, "sound"), "sound"),
            new NbtTag<int>("min_delay", minDelay),
            new NbtTag<int>("max_delay", maxDelay)
        };

        if (TryGet(value, "replace_current_music", out var replace)
            && Bool(replace, Field(path, "replace_current_music")))
        {
            music.Add(Byte("replace_current_music", true));
        }

        return music;
    }

    // Vanilla's AmbientSounds: an optional loop and mood, and additions, a list or a single one (written as a single
    // one when there's one, and left out when there are none).
    private static NbtCompound ReadAmbientSounds(JsonElement value, string path, string name)
    {
        Object(value, path);

        var sounds = new NbtCompound(name);

        if (TryGet(value, "loop", out var loop))
            sounds.Add(ReadSound(loop, Field(path, "loop"), "loop"));

        if (TryGet(value, "mood", out var mood))
            sounds.Add(ReadMood(mood, Field(path, "mood")));

        if (!TryGet(value, "additions", out var additions))
            return sounds;

        var additionsPath = Field(path, "additions");
        if (additions.ValueKind != JsonValueKind.Array)
        {
            sounds.Add(ReadAddition(additions, additionsPath, "additions"));
            return sounds;
        }

        var count = additions.GetArrayLength();
        if (count == 1)
        {
            sounds.Add(ReadAddition(additions[0], Index(additionsPath, 0), "additions"));
        }
        else if (count > 1)
        {
            var list = new NbtList(NbtTagType.Compound, "additions");
            for (var i = 0; i < count; i++)
                list.Add(ReadAddition(additions[i], Index(additionsPath, i), ""));

            sounds.Add(list);
        }

        return sounds;
    }

    private static NbtCompound ReadMood(JsonElement value, string path)
    {
        Object(value, path);

        var tickDelay = Int(Required(value, path, "tick_delay"), Field(path, "tick_delay"));
        var searchExtent = Int(Required(value, path, "block_search_extent"), Field(path, "block_search_extent"));
        var offset = Double(Required(value, path, "offset"), Field(path, "offset"));

        return new NbtCompound("mood")
        {
            ReadSound(Required(value, path, "sound"), Field(path, "sound"), "sound"),
            new NbtTag<int>("tick_delay", tickDelay),
            new NbtTag<int>("block_search_extent", searchExtent),
            new NbtTag<double>("offset", offset)
        };
    }

    private static NbtCompound ReadAddition(JsonElement value, string path, string name)
    {
        Object(value, path);

        var tickChance = Double(Required(value, path, "tick_chance"), Field(path, "tick_chance"));

        return new NbtCompound(name)
        {
            ReadSound(Required(value, path, "sound"), Field(path, "sound"), "sound"),
            new NbtTag<double>("tick_chance", tickChance)
        };
    }

    // A sound event like vanilla's SoundEvent.CODEC: one of vanilla's by identifier, written as its identifier, or
    // {"sound_id": identifier, "range": float}, whose range is left out if it isn't a number, as vanilla does.
    private static INbtTag ReadSound(JsonElement value, string path, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var id = Identifier(value, path);

            if (!RegistryIds.IsSoundEvent(id))
                throw Error(path, $"{id} isn't one of vanilla's sound events; give others as {{\"sound_id\": ...}}");

            return new NbtTag<string>(name, id);
        }

        if (value.ValueKind != JsonValueKind.Object)
            throw Error(path, "expected a sound event's identifier or {\"sound_id\": ...}");

        var sound = new NbtCompound(name)
        {
            new NbtTag<string>("sound_id", Identifier(Required(value, path, "sound_id"), Field(path, "sound_id")))
        };

        if (TryGet(value, "range", out var range) && range.ValueKind == JsonValueKind.Number)
            sound.Add(new NbtTag<float>("range", Float(range, Field(path, "range"))));

        return sound;
    }
}
