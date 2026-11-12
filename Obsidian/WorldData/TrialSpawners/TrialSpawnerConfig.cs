using Obsidian.Nbt;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// How a trial spawner spawns and rewards, like vanilla's <c>TrialSpawnerConfig</c>: a <c>trial_spawner</c> registry
/// entry or a config saved inline in the block entity.
/// </summary>
/// <param name="SpawnRange">How far from the spawner, along x and z, a mob may spawn.</param>
/// <param name="TotalMobs">How many mobs a trial with one player spawns in all.</param>
/// <param name="SimultaneousMobs">How many of them may be alive at once with one player.</param>
/// <param name="TotalMobsAddedPerPlayer">How many more mobs each further player adds.</param>
/// <param name="SimultaneousMobsAddedPerPlayer">How many more may be alive at once for each further player.</param>
/// <param name="TicksBetweenSpawn">The ticks after one mob spawns, or a tracked one goes, before the next.</param>
/// <param name="SpawnPotentials">The spawn data mobs are spawned from (vanilla's <c>SpawnData</c>, saved form).</param>
/// <param name="LootTablesToEject">The loot tables a finished trial's rewards come from.</param>
/// <param name="ItemsToDropWhenOminous">The loot table an ominous trial's dropped items come from.</param>
internal sealed record TrialSpawnerConfig(
    int SpawnRange,
    float TotalMobs,
    float SimultaneousMobs,
    float TotalMobsAddedPerPlayer,
    float SimultaneousMobsAddedPerPlayer,
    int TicksBetweenSpawn,
    WeightedEntries<NbtCompound> SpawnPotentials,
    WeightedEntries<string> LootTablesToEject,
    string ItemsToDropWhenOminous)
{
    /// <summary>Vanilla's <c>TrialSpawnerConfig.DEFAULT</c>, which a config's missing fields take.</summary>
    public static TrialSpawnerConfig Default { get; } = new(
        4,
        6f,
        2f,
        2f,
        1f,
        40,
        WeightedEntries<NbtCompound>.Empty,
        new WeightedEntries<string>(
        [
            ("minecraft:spawners/trial_chamber/consumables", 1),
            ("minecraft:spawners/trial_chamber/key", 1)
        ]),
        "minecraft:spawners/trial_chamber/items_to_drop_when_ominous");

    private static readonly Lazy<FrozenDictionary<string, TrialSpawnerConfig>> registry = new(LoadRegistry);

    /// <summary>
    /// How many mobs a trial spawns in all with <paramref name="additionalPlayers"/> players besides the first, as
    /// vanilla's <c>calculateTargetTotalMobs</c> works it out: in single precision, rounded down.
    /// </summary>
    public int TargetTotalMobs(int additionalPlayers) =>
        (int)Math.Floor(this.TotalMobs + this.TotalMobsAddedPerPlayer * additionalPlayers);

    /// <summary>How many mobs may be alive at once, like vanilla's <c>calculateTargetSimultaneousMobs</c>.</summary>
    public int TargetSimultaneousMobs(int additionalPlayers) =>
        (int)Math.Floor(this.SimultaneousMobs + this.SimultaneousMobsAddedPerPlayer * additionalPlayers);

    /// <summary>The ticks between an ominous trial's dropped items, which vanilla fixes at 160.</summary>
    public static long TicksBetweenItemSpawners => 160;

    /// <summary>Whether every field is the default's, so vanilla leaves the config out of the save.</summary>
    public bool IsDefault =>
        this.SpawnRange == Default.SpawnRange
        && SameFloat(this.TotalMobs, Default.TotalMobs)
        && SameFloat(this.SimultaneousMobs, Default.SimultaneousMobs)
        && SameFloat(this.TotalMobsAddedPerPlayer, Default.TotalMobsAddedPerPlayer)
        && SameFloat(this.SimultaneousMobsAddedPerPlayer, Default.SimultaneousMobsAddedPerPlayer)
        && this.TicksBetweenSpawn == Default.TicksBetweenSpawn
        && this.SpawnPotentials.IsEmpty && this.SpawnPotentials.Entries.IsEmpty
        && SameLootTables(this.LootTablesToEject, Default.LootTablesToEject)
        && this.ItemsToDropWhenOminous == Default.ItemsToDropWhenOminous;

    /// <summary>
    /// The config written out inline, as vanilla's codec writes it: each field that isn't its default's.
    /// </summary>
    public NbtCompound Write(string name)
    {
        var tag = new NbtCompound(name);
        if (this.SpawnRange != Default.SpawnRange)
            tag.Add(new NbtTag<int>("spawn_range", this.SpawnRange));

        AddFloat(tag, "total_mobs", this.TotalMobs, Default.TotalMobs);
        AddFloat(tag, "simultaneous_mobs", this.SimultaneousMobs, Default.SimultaneousMobs);
        AddFloat(tag, "total_mobs_added_per_player", this.TotalMobsAddedPerPlayer, Default.TotalMobsAddedPerPlayer);
        AddFloat(
            tag,
            "simultaneous_mobs_added_per_player",
            this.SimultaneousMobsAddedPerPlayer,
            Default.SimultaneousMobsAddedPerPlayer);
        if (this.TicksBetweenSpawn != Default.TicksBetweenSpawn)
            tag.Add(new NbtTag<int>("ticks_between_spawn", this.TicksBetweenSpawn));

        if (!this.SpawnPotentials.Entries.IsEmpty)
            tag.Add(WriteWeighted("spawn_potentials", this.SpawnPotentials, data => DataBlockEntity.Copy(data)));

        if (!SameLootTables(this.LootTablesToEject, Default.LootTablesToEject))
        {
            var tables = WriteWeighted(
                "loot_tables_to_eject",
                this.LootTablesToEject,
                id => new NbtTag<string>(null, id));
            tag.Add(tables);
        }

        if (this.ItemsToDropWhenOminous != Default.ItemsToDropWhenOminous)
            tag.Add(new NbtTag<string>("items_to_drop_when_ominous", this.ItemsToDropWhenOminous));

        return tag;
    }

    // Vanilla compares a config's numbers to the defaults' as boxed floats: by their bits.
    private static bool SameFloat(float left, float right) =>
        float.IsNaN(left) && float.IsNaN(right)
        || BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static void AddFloat(NbtCompound tag, string name, float value, float fallback)
    {
        if (!SameFloat(value, fallback))
            tag.Add(new NbtTag<float>(name, value));
    }

    private static bool SameLootTables(WeightedEntries<string> left, WeightedEntries<string> right) =>
        left.Entries.SequenceEqual(right.Entries);

    private static NbtList WriteWeighted<T>(string name, WeightedEntries<T> entries, Func<T, INbtTag> write)
    {
        var list = new NbtList(NbtTagType.Compound, name);
        foreach (var (value, weight) in entries.Entries)
        {
            var data = write(value);
            data.Name = "data";
            list.Add(new NbtCompound { data, new NbtTag<int>("weight", weight) });
        }

        return list;
    }

    /// <summary>
    /// The config a saved <c>normal_config</c> or <c>ominous_config</c> names: a registry entry's id, or a config
    /// written out inline, whose fields that don't read take their defaults.
    /// </summary>
    /// <returns>Null when it can't be read at all: an unknown id, or neither an id nor a config.</returns>
    public static TrialSpawnerConfig? Read(INbtTag saved) => saved switch
    {
        NbtTag<string> id => registry.Value.GetValueOrDefault(id.Value ?? string.Empty),
        NbtCompound inline => FromNbt(inline),
        _ => null
    };

    // Vanilla's DIRECT_CODEC on NBT, as vanilla's block entity reads it (its partial result): each field optional, with
    // its default, and a field that doesn't read (the wrong type, or out of its range) takes its default too, while
    // the others keep theirs. A list drops the entries that don't read. Captured by trial_spawners.json's config cases.
    private static TrialSpawnerConfig FromNbt(NbtCompound tag)
    {
        var fields = new LenientFields(tag);
        return Default with
        {
            SpawnRange = fields.Number("spawn_range", Default.SpawnRange, range => range is >= 1 and <= 128),
            TotalMobs = fields.Number("total_mobs", Default.TotalMobs, NotNegative),
            SimultaneousMobs = fields.Number("simultaneous_mobs", Default.SimultaneousMobs, NotNegative),
            TotalMobsAddedPerPlayer = fields.Number(
                "total_mobs_added_per_player",
                Default.TotalMobsAddedPerPlayer,
                NotNegative),
            SimultaneousMobsAddedPerPlayer = fields.Number(
                "simultaneous_mobs_added_per_player",
                Default.SimultaneousMobsAddedPerPlayer,
                NotNegative),
            TicksBetweenSpawn = fields.Number("ticks_between_spawn", Default.TicksBetweenSpawn, ticks => ticks >= 0),
            SpawnPotentials = fields.Weighted("spawn_potentials", Default.SpawnPotentials, SpawnDataOf),
            LootTablesToEject = fields.Weighted("loot_tables_to_eject", Default.LootTablesToEject, LootTableOf),
            ItemsToDropWhenOminous = fields.Identifier("items_to_drop_when_ominous", Default.ItemsToDropWhenOminous)
        };
    }

    // Vanilla's floatRange(0, Float.MAX_VALUE).
    private static bool NotNegative(float value) => value is >= 0 and <= float.MaxValue;

    // The ranges vanilla's codec checks.
    private static bool IsValid(TrialSpawnerConfig config) =>
        config.SpawnRange is >= 1 and <= 128
        && config.TotalMobs >= 0
        && config.SimultaneousMobs >= 0
        && config.TotalMobsAddedPerPlayer >= 0
        && config.SimultaneousMobsAddedPerPlayer >= 0
        && config.TicksBetweenSpawn >= 0;

    /// <summary>
    /// Vanilla's <c>SpawnData</c> codec: an entity compound, and optional spawn rules and equipment of their own
    /// shapes, or null when it doesn't read. The equipment's loot table is written back with its namespace, as vanilla
    /// writes it.
    /// </summary>
    internal static NbtCompound? SpawnDataOf(INbtTag data)
    {
        if (data is not NbtCompound spawnData || !spawnData.TryGetTag<NbtCompound>("entity", out _))
            return null;

        if (spawnData.TryGetTag("custom_spawn_rules", out var rules) && rules is not NbtCompound)
            return null;

        string? table = null;
        if (spawnData.TryGetTag("equipment", out var equipment)
            && (equipment is not NbtCompound saved || !saved.TryGetTagValue<string>("loot_table", out var id)
                || (table = LootTableId(id)) is null))
        {
            return null;
        }

        var copy = DataBlockEntity.Copy(spawnData);
        if (table is not null && copy.TryGetTag<NbtCompound>("equipment", out var copied)
            && copied.TryGetTagValue<string>("loot_table", out var written) && written != table)
        {
            Entities.EntityNbt.Set(copied, new NbtTag<string>("loot_table", table));
        }

        return copy;
    }

    private static string? LootTableOf(INbtTag data) =>
        data is NbtTag<string> { Value: string id } ? LootTableId(id) : null;

    /// <summary>
    /// Vanilla's <c>ResourceKey</c> codec for a loot table: an identifier, in the <c>minecraft</c> namespace when it
    /// names none, which vanilla saves with its namespace. Null when it isn't an identifier.
    /// </summary>
    internal static string? LootTableId(string id)
    {
        if (!IsIdentifier(id))
            return null;

        var colon = id.IndexOf(':');
        return colon switch
        {
            < 0 => "minecraft:" + id,
            0 => "minecraft" + id,
            _ => id
        };
    }

    // Vanilla's Identifier: an optional namespace of [a-z0-9_.-] (but not "..") and a colon, then a path that may also
    // hold slashes.
    private static bool IsIdentifier(string id)
    {
        var colon = id.IndexOf(':');
        var space = colon < 0 ? string.Empty : id[..colon];
        var path = colon < 0 ? id : id[(colon + 1)..];
        return space != ".."
            && space.All(character => IsIdentifierCharacter(character, slash: false))
            && path.All(character => IsIdentifierCharacter(character, slash: true));
    }

    private static bool IsIdentifierCharacter(char character, bool slash) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-' || slash && character == '/';

    /// <summary>
    /// Reads a saved config's fields as vanilla's block entity does, from its codec's partial result: a field that's
    /// missing, or there but doesn't read, takes its default.
    /// </summary>
    private sealed class LenientFields(NbtCompound tag)
    {
        public T Number<T>(string name, T fallback, Func<T, bool> inRange)
            where T : struct, System.Numerics.INumberBase<T> =>
            tag.TryGetTag(name, out var field) && field.TryGetNumber<T>(out var value) && inRange(value)
                ? value
                : fallback;

        public string Identifier(string name, string fallback) =>
            tag.TryGetTag(name, out var field) && LootTableOf(field) is string id ? id : fallback;

        // Vanilla's WeightedList codec: a list of {data, weight}; an entry without readable data or with a weight below
        // 0 is dropped, and a field that isn't a list takes its default.
        public WeightedEntries<T> Weighted<T>(string name, WeightedEntries<T> fallback, Func<INbtTag, T?> read)
            where T : class
        {
            if (!tag.TryGetTag(name, out var field) || field is not NbtList entries)
                return fallback;

            var values = ImmutableArray.CreateBuilder<(T, int)>();
            foreach (var element in entries.OfType<NbtCompound>())
            {
                var data = element.TryGetTag("data", out var saved) ? read(saved) : null;
                var weight = 0;
                var weighted = element.TryGetTag("weight", out var savedWeight)
                    && savedWeight.TryGetNumber(out weight)
                    && weight >= 0;
                if (data is not null && weighted)
                    values.Add((data, weight));
            }

            return new WeightedEntries<T>(values.ToImmutable());
        }
    }

    private static FrozenDictionary<string, TrialSpawnerConfig> LoadRegistry() =>
        ItemUseOnBehaviors.Load("trial_spawner.json")
            .Select(entry => KeyValuePair.Create(entry.Key, FromJson(entry.Value)))
            .Where(entry => entry.Value is not null)
            .ToFrozenDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);

    // A registry entry, read from its JSON as vanilla's data pack loader reads it.
    private static TrialSpawnerConfig? FromJson(JsonElement json)
    {
        var config = Default with
        {
            SpawnRange = json.TryGetProperty("spawn_range", out var range) ? range.GetInt32() : Default.SpawnRange,
            TotalMobs = JsonFloat(json, "total_mobs", Default.TotalMobs),
            SimultaneousMobs = JsonFloat(json, "simultaneous_mobs", Default.SimultaneousMobs),
            TotalMobsAddedPerPlayer = JsonFloat(json, "total_mobs_added_per_player", Default.TotalMobsAddedPerPlayer),
            SimultaneousMobsAddedPerPlayer = JsonFloat(
                json,
                "simultaneous_mobs_added_per_player",
                Default.SimultaneousMobsAddedPerPlayer),
            TicksBetweenSpawn = json.TryGetProperty("ticks_between_spawn", out var ticks)
                ? ticks.GetInt32()
                : Default.TicksBetweenSpawn,
            SpawnPotentials = json.TryGetProperty("spawn_potentials", out var potentials)
                ? JsonWeighted(potentials, SpawnDataFromJson)
                : Default.SpawnPotentials,
            LootTablesToEject = json.TryGetProperty("loot_tables_to_eject", out var tables)
                ? JsonWeighted(tables, data => LootTableId(data.GetString()!) ?? data.GetString()!)
                : Default.LootTablesToEject,
            ItemsToDropWhenOminous = json.TryGetProperty("items_to_drop_when_ominous", out var ominous)
                ? LootTableId(ominous.GetString()!) ?? ominous.GetString()!
                : Default.ItemsToDropWhenOminous
        };

        return IsValid(config) ? config : null;
    }

    private static float JsonFloat(JsonElement json, string name, float fallback) =>
        json.TryGetProperty(name, out var value) ? value.GetSingle() : fallback;

    private static WeightedEntries<T> JsonWeighted<T>(JsonElement entries, Func<JsonElement, T> read)
    {
        var values = ImmutableArray.CreateBuilder<(T, int)>();
        foreach (var entry in entries.EnumerateArray())
            values.Add((read(entry.GetProperty("data")), entry.GetProperty("weight").GetInt32()));

        return new WeightedEntries<T>(values.ToImmutable());
    }

    /// <summary>
    /// A spawn data's JSON as the NBT vanilla saves it in <c>spawn_data</c>: its entity converted as vanilla converts
    /// JSON to NBT (see <see cref="DataNumber"/>), and its spawn rules and equipment as their codecs write them.
    /// </summary>
    private static NbtCompound SpawnDataFromJson(JsonElement json)
    {
        var data = new NbtCompound();
        if (json.TryGetProperty("entity", out var entity))
            data.Add(Converted("entity", entity));

        if (json.TryGetProperty("custom_spawn_rules", out var rules))
            data.Add(Ints("custom_spawn_rules", rules));

        if (json.TryGetProperty("equipment", out var equipment))
            data.Add(Equipment(equipment));

        return data;
    }

    // Vanilla's EquipmentTable codec: the loot table, and one drop chance for every slot or one for each slot named.
    private static NbtCompound Equipment(JsonElement json)
    {
        var equipment = new NbtCompound("equipment")
        {
            new NbtTag<string>("loot_table", LootTableId(json.GetProperty("loot_table").GetString()!)
                ?? json.GetProperty("loot_table").GetString())
        };

        if (!json.TryGetProperty("slot_drop_chances", out var chances))
            return equipment;

        if (chances.ValueKind == JsonValueKind.Number)
        {
            equipment.Add(new NbtTag<float>("slot_drop_chances", chances.GetSingle()));
            return equipment;
        }

        var slots = new NbtCompound("slot_drop_chances");
        foreach (var slot in chances.EnumerateObject())
            slots.Add(new NbtTag<float>(slot.Name, slot.Value.GetSingle()));

        equipment.Add(slots);
        return equipment;
    }

    // Integer ranges, as vanilla's codecs write a spawn data's light limits: whole numbers as ints.
    private static INbtTag Ints(string name, JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
            return new NbtTag<int>(name, json.GetInt32());

        var compound = new NbtCompound(name);
        foreach (var property in json.EnumerateObject())
            compound.Add(Ints(property.Name, property.Value));

        return compound;
    }

    /// <summary>
    /// JSON as vanilla's data fixer converts it to NBT (<c>JsonOps.convertTo</c>), which is how a registry entry's
    /// entity data becomes the compound vanilla saves: whole numbers as the smallest of byte, short, int and long
    /// that holds them, other numbers as floats when a float holds them exactly and doubles otherwise, booleans as
    /// bytes, and arrays of one kind of whole number as arrays. This must match, as it decides the saved data.
    /// </summary>
    private static INbtTag Converted(string name, JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                var compound = new NbtCompound(name);
                foreach (var property in json.EnumerateObject())
                    compound.Add(Converted(property.Name, property.Value));

                return compound;
            case JsonValueKind.Array:
                return ConvertedArray(name, json);
            case JsonValueKind.String:
                return new NbtTag<string>(name, json.GetString());
            case JsonValueKind.True or JsonValueKind.False:
                return new NbtTag<byte>(name, json.GetBoolean() ? (byte)1 : (byte)0);
            default:
                return DataNumber(name, json.GetRawText());
        }
    }

    private static INbtTag ConvertedArray(string name, JsonElement json)
    {
        var elements = json.EnumerateArray().Select(element => Converted(string.Empty, element)).ToList();
        if (elements.Count > 0 && elements.All(element => element is NbtTag<byte>))
            return new NbtArray<byte>(name, [.. elements.Select(element => ((NbtTag<byte>)element).Value)]);

        if (elements.Count > 0 && elements.All(element => element is NbtTag<int>))
            return new NbtArray<int>(name, [.. elements.Select(element => ((NbtTag<int>)element).Value)]);

        if (elements.Count > 0 && elements.All(element => element is NbtTag<long>))
            return new NbtArray<long>(name, [.. elements.Select(element => ((NbtTag<long>)element).Value)]);

        var list = new NbtList(elements.Count == 0 ? NbtTagType.End : elements[0].Type, name);
        foreach (var element in elements)
        {
            element.Name = null;
            list.Add(element);
        }

        return list;
    }

    /// <summary>A JSON number as vanilla's data fixer converts it (see <see cref="Converted"/>).</summary>
    private static INbtTag DataNumber(string name, string text)
    {
        var exact = decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        if (exact && value == decimal.Truncate(value) && value >= long.MinValue && value <= long.MaxValue)
        {
            var whole = (long)value;
            if (whole is >= sbyte.MinValue and <= sbyte.MaxValue)
                return new NbtTag<byte>(name, unchecked((byte)(sbyte)whole));

            if (whole is >= short.MinValue and <= short.MaxValue)
                return new NbtTag<short>(name, (short)whole);

            if (whole is >= int.MinValue and <= int.MaxValue)
                return new NbtTag<int>(name, (int)whole);

            return new NbtTag<long>(name, whole);
        }

        var number = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (float)number == number ? new NbtTag<float>(name, (float)number) : new NbtTag<double>(name, number);
    }
}
