using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Nbt;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// A plugin's biome as vanilla 26.3's Biome.DIRECT_CODEC reads a data pack's: its climate, attributes, effects,
/// generation and mob spawns.
/// </summary>
/// <remarks>
/// Vanilla logs and drops a carver, feature or spawner it can't read; a plugin's file is rejected instead. Placed
/// features and configured carvers are vanilla's or plugins' identifiers, a tag, or defined inline, which are kept as
/// their JSON and read with the plugins' features (see <see cref="PluginFeatures"/>). Tags are resolved with the
/// features, since they may be plugins' tags.
/// </remarks>
internal sealed record BiomeData(
    bool HasPrecipitation,
    float Temperature,
    string TemperatureModifier,
    float Downfall,
    EnvironmentAttributeMap Attributes,
    int WaterColor,
    int? FoliageColor,
    int? DryFoliageColor,
    int? GrassColor,
    string GrassColorModifier,
    ImmutableArray<string> Carvers,
    ImmutableArray<ImmutableArray<string>> Features,
    float CreatureSpawnProbability,
    Dictionary<string, SpawnerMob[]> Spawners,
    Dictionary<string, SpawnCost> SpawnCosts)
{
    private static readonly string[] temperatureModifiers = ["none", "frozen"];

    private static readonly string[] grassColorModifiers = ["none", "dark_forest", "swamp"];

    // Vanilla's MobCategory names, the keys of a biome's spawners.
    private static readonly string[] mobCategories =
    [
        "monster", "creature", "ambient", "axolotls", "underground_water_creature", "water_creature",
        "water_ambient", "misc"
    ];

    /// <param name="isPlacedFeature">Whether a placed feature exists; vanilla's by default.</param>
    /// <param name="isConfiguredCarver">Whether a configured carver exists; vanilla's by default.</param>
    public static BiomeData Read(
        JsonElement root,
        Func<string, bool>? isPlacedFeature = null,
        Func<string, bool>? isConfiguredCarver = null)
    {
        Object(root, "");
        root = MigrateLegacySpawns(root);

        var effects = Object(Required(root, "", "effects"), "effects");
        var attributes = TryGet(root, "attributes", out var attributesValue)
            ? EnvironmentAttributes.Read(attributesValue, "attributes", onlyPositional: true)
            : EnvironmentAttributeMap.Empty;

        var carvers = ReadHolderSet(
            Required(root, "", "carvers"),
            "carvers",
            "worldgen/carver",
            isConfiguredCarver ?? RegistryIds.IsConfiguredCarver,
            inline: true);

        var (spawners, spawnCosts) = ReadBiomeSpawns(root);
        var spawnProbability = attributes.Apply("minecraft:gameplay/creature_world_gen_spawn_probability", 0.1f);

        return new BiomeData(
            HasPrecipitation: Bool(Required(root, "", "has_precipitation"), "has_precipitation"),
            Temperature: Float(Required(root, "", "temperature"), "temperature"),
            TemperatureModifier: OptionalEnum(root, "", "temperature_modifier", temperatureModifiers),
            Downfall: Float(Required(root, "", "downfall"), "downfall"),
            Attributes: attributes,
            WaterColor: RgbColor(Required(effects, "effects", "water_color"), "effects.water_color"),
            FoliageColor: OptionalColor(effects, "foliage_color"),
            DryFoliageColor: OptionalColor(effects, "dry_foliage_color"),
            GrassColor: OptionalColor(effects, "grass_color"),
            GrassColorModifier: OptionalEnum(effects, "effects", "grass_color_modifier", grassColorModifiers),
            Carvers: carvers,
            Features: ReadFeatures(
                Required(root, "", "features"),
                "features",
                isPlacedFeature ?? RegistryIds.IsPlacedFeature),
            CreatureSpawnProbability: Math.Clamp(spawnProbability, 0f, 0.9999999f),
            Spawners: spawners,
            SpawnCosts: spawnCosts);
    }

    private static JsonElement MigrateLegacySpawns(JsonElement root)
    {
        var hasSpawners = TryGet(root, "spawners", out var spawners);
        var hasCosts = TryGet(root, "spawn_costs", out var costs);
        var hasProbability = TryGet(root, "creature_spawn_probability", out var probability);

        if (!hasSpawners && !hasCosts && !hasProbability)
            return root;

        var attributes = TryGet(root, "attributes", out var attributesValue)
            ? ToNode(Object(attributesValue, "attributes")).AsObject()
            : new JsonObject();

        if (hasSpawners || hasCosts)
        {
            const string attribute = "minecraft:gameplay/natural_mob_spawns";
            RejectLegacySpawnConflict(attributes, "spawners, spawn_costs", attribute);

            var settings = new JsonObject
            {
                ["spawns_by_category"] = hasSpawners ? MigrateLegacySpawners(spawners) : new JsonObject(),
                ["spawn_costs"] = hasCosts ? ToNode(costs) : new JsonObject()
            };

            // Like 26.3's vanilla biomes, overlay the dimension's categories and costs with this biome's entries.
            attributes[attribute] = new JsonObject
            {
                ["modifier"] = "overlay",
                ["argument"] = settings
            };
        }

        if (hasProbability)
        {
            const string attribute = "minecraft:gameplay/creature_world_gen_spawn_probability";
            RejectLegacySpawnConflict(attributes, "creature_spawn_probability", attribute);
            attributes[attribute] = ToNode(probability);
        }

        // Reuse the 26.3 readers so migrated attributes and the server's spawn model have the same validation.
        var converted = ToNode(root).AsObject();
        converted["attributes"] = attributes;
        converted.Remove("spawners");
        converted.Remove("spawn_costs");
        converted.Remove("creature_spawn_probability");

        return JsonSerializer.SerializeToElement(converted);
    }

    // Copies an element already read (leniently, like vanilla's Gson: comments and trailing commas allowed) into a
    // node without parsing its raw text again, which would reject those.
    private static JsonNode ToNode(JsonElement element) => JsonSerializer.SerializeToNode(element)!;

    private static void RejectLegacySpawnConflict(JsonObject attributes, string fields, string attribute)
    {
        foreach (var property in attributes)
        {
            if (TryParseIdentifier(property.Key, out var id) && id == attribute)
            {
                throw Error(fields, $"cannot combine legacy fields with attributes.{attribute}; "
                    + $"use only attributes.{attribute} in 26.3");
            }
        }
    }

    private static JsonObject MigrateLegacySpawners(JsonElement value)
    {
        Object(value, "spawners");

        var categories = new JsonObject();

        foreach (var property in value.EnumerateObject())
        {
            var categoryPath = Field("spawners", property.Name);
            var list = Array(property.Value, categoryPath);
            var mobs = new JsonArray();

            foreach (var item in list.EnumerateArray())
            {
                var mob = ReadSpawner(item, Index(categoryPath, mobs.Count));
                var count = TryGet(item, "count", out var provider)
                    ? ToNode(provider)
                    : new JsonObject
                    {
                        ["type"] = IntProviderTypes.Uniform,
                        ["min_inclusive"] = mob.MinCount,
                        ["max_inclusive"] = mob.MaxCount
                    };

                mobs.Add(new JsonObject
                {
                    ["type"] = mob.Type,
                    ["weight"] = mob.Weight,
                    ["count"] = count
                });
            }

            categories[property.Name] = mobs;
        }

        return categories;
    }

    // Spawns are server-only attributes in 26.3. Keep their providers in the attribute map as well as exposing the
    // biome's own values through the existing server model. Dimension/biome overlay composition belongs to spawning.
    private static (Dictionary<string, SpawnerMob[]> Spawners, Dictionary<string, SpawnCost> Costs)
        ReadBiomeSpawns(JsonElement root)
    {
        if (!TryGet(root, "attributes", out var attributes))
            return ([], []);

        foreach (var property in attributes.EnumerateObject().Reverse())
        {
            if (!TryParseIdentifier(property.Name, out var id) || id != "minecraft:gameplay/natural_mob_spawns")
                continue;

            var value = property.Value;

            // Match the attribute codec's precedence: a complete value wins even if it has unknown modifier fields.
            try
            {
                return ReadSpawnSettings(value, "attributes.natural_mob_spawns");
            }
            catch (InvalidDataException) when (TryGet(value, "modifier", out _))
            {
                var argument = Required(value, "attributes.natural_mob_spawns", "argument");
                return ReadSpawnSettings(argument, "attributes.natural_mob_spawns.argument");
            }
        }

        return ([], []);
    }

    internal static (Dictionary<string, SpawnerMob[]> Spawners, Dictionary<string, SpawnCost> Costs)
        ReadSpawnSettings(JsonElement value, string path)
    {
        Object(value, path);

        var spawners = ReadSpawners(
            Required(value, path, "spawns_by_category"),
            Field(path, "spawns_by_category"));
        var costs = ReadSpawnCosts(Required(value, path, "spawn_costs"), Field(path, "spawn_costs"));

        return (spawners, costs);
    }

    internal static NbtCompound ReadMobSpawns(JsonElement value, string path, string name)
    {
        var (spawners, costs) = ReadSpawnSettings(value, path);
        var categories = new NbtCompound("spawns_by_category");

        foreach (var (category, mobs) in spawners)
        {
            var list = new NbtList(NbtTagType.Compound, category);

            foreach (var mob in mobs)
            {
                list.Add(new NbtCompound
                {
                    new NbtTag<string>("type", mob.Type),
                    IntProviderCodec.ToNbt(mob.Count!, "count"),
                    new NbtTag<int>("weight", mob.Weight)
                });
            }

            categories.Add(list);
        }

        var spawnCosts = new NbtCompound("spawn_costs");

        foreach (var (type, cost) in costs)
        {
            spawnCosts.Add(new NbtCompound(type)
            {
                new NbtTag<double>("energy_budget", cost.EnergyBudget),
                new NbtTag<double>("charge", cost.Charge)
            });
        }

        var result = new NbtCompound(name) { categories, spawnCosts };
        WorldData.SpawnEnvironment.Remember(result, spawners, costs);
        return result;
    }

    /// <summary>
    /// The biome as the server keeps it, with its attribute map attached for the attributes the server applies where
    /// it is (see <see cref="WorldData.LevelEnvironment"/>).
    /// </summary>
    public BiomeElement ToElement()
    {
        var element = this.CreateElement();
        WorldData.LevelEnvironment.Attach(element, this.Attributes);

        return element;
    }

    private BiomeElement CreateElement() => new()
    {
        HasPrecipitation = this.HasPrecipitation,
        Temperature = this.Temperature,
        TemperatureModifier = this.TemperatureModifier,
        Downfall = this.Downfall,
        Effects = new BiomeEffect
        {
            WaterColor = FormatRgb(this.WaterColor),
            FoliageColor = this.FoliageColor is int foliage ? FormatRgb(foliage) : null,
            DryFoliageColor = this.DryFoliageColor is int dryFoliage ? FormatRgb(dryFoliage) : null,
            GrassColor = this.GrassColor is int grass ? FormatRgb(grass) : null,
            GrassColorModifier = this.GrassColorModifier
        },
        Carvers = this.Carvers,
        Features = this.Features,
        Spawners = this.Spawners,
        SpawnCosts = this.SpawnCosts,
        CreatureSpawnProbability = this.CreatureSpawnProbability
    };

    /// <summary>
    /// The biome as vanilla's Biome.NETWORK_CODEC writes it, which is what clients decode: the climate, the synced
    /// attributes and the effects. Defaults vanilla leaves out are left out.
    /// </summary>
    public NbtCompound ToNetworkNbt()
    {
        var compound = new NbtCompound
        {
            new NbtTag<byte>("has_precipitation", this.HasPrecipitation ? (byte)1 : (byte)0),
            new NbtTag<float>("temperature", this.Temperature),
            new NbtTag<float>("downfall", this.Downfall)
        };

        if (this.TemperatureModifier != "none")
            compound.Add(new NbtTag<string>("temperature_modifier", this.TemperatureModifier));

        if (this.Attributes.Entries.Length > 0)
            compound.Add(this.Attributes.ToNetworkNbt("attributes"));

        var effects = new NbtCompound("effects")
        {
            new NbtTag<string>("water_color", FormatRgb(this.WaterColor))
        };

        if (this.FoliageColor is int foliage)
            effects.Add(new NbtTag<string>("foliage_color", FormatRgb(foliage)));

        if (this.DryFoliageColor is int dryFoliage)
            effects.Add(new NbtTag<string>("dry_foliage_color", FormatRgb(dryFoliage)));

        if (this.GrassColor is int grass)
            effects.Add(new NbtTag<string>("grass_color", FormatRgb(grass)));

        if (this.GrassColorModifier != "none")
            effects.Add(new NbtTag<string>("grass_color_modifier", this.GrassColorModifier));

        compound.Add(effects);

        return compound;
    }

    /// <summary>
    /// Reads a holder set like vanilla's RegistryCodecs.homogeneousList: a <c>#tag</c>, an identifier, or a list of
    /// identifiers. Vanilla also takes entries defined inline, which are kept as their JSON text where
    /// <paramref name="inline"/> allows them.
    /// </summary>
    /// <returns>
    /// The identifiers and inline entries' JSON, or the tag as a single <c>#tag</c>, which isn't checked here: plugins'
    /// tags are applied after their files are read.
    /// </returns>
    public static ImmutableArray<string> ReadHolderSet(
        JsonElement value,
        string path,
        string registry,
        Func<string, bool> exists,
        bool inline = false)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith('#'))
            return [$"#{Tag(value, path)}"];

        if (value.ValueKind != JsonValueKind.Array)
            return [ReadReference(value, path, registry, exists, inline)];

        var ids = ImmutableArray.CreateBuilder<string>();

        for (var i = 0; i < value.GetArrayLength(); i++)
            ids.Add(ReadReference(value[i], Index(path, i), registry, exists, inline));

        return ids.ToImmutable();
    }

    private static int? OptionalColor(JsonElement effects, string name) =>
        TryGet(effects, name, out var value) ? RgbColor(value, Field("effects", name)) : null;

    // An optional enum field, whose default is its first name.
    private static string OptionalEnum(JsonElement obj, string path, string name, string[] names) =>
        TryGet(obj, name, out var value) ? Enum(value, Field(path, name), names) : names[0];

    // A list of holder sets of placed features, one per generation step.
    private static ImmutableArray<ImmutableArray<string>> ReadFeatures(
        JsonElement value,
        string path,
        Func<string, bool> isPlacedFeature)
    {
        Array(value, path);

        var steps = ImmutableArray.CreateBuilder<ImmutableArray<string>>();

        for (var i = 0; i < value.GetArrayLength(); i++)
        {
            var step = ReadHolderSet(value[i], Index(path, i), "worldgen/placed_feature", isPlacedFeature, true);
            steps.Add(step);
        }

        return steps.ToImmutable();
    }

    private static string ReadReference(
        JsonElement value,
        string path,
        string registry,
        Func<string, bool> exists,
        bool inline)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            return inline
                ? value.GetRawText()
                : throw Error(path, $"plugins can't define a {registry} inline yet; refer to one by its identifier");
        }

        var id = Identifier(value, path);

        return exists(id) ? id : throw Error(path, $"{id} isn't a {registry}");
    }

    // Vanilla 26.3's weighted spawners, by mob category.
    private static Dictionary<string, SpawnerMob[]> ReadSpawners(JsonElement value, string path)
    {
        Object(value, path);

        var spawners = new Dictionary<string, SpawnerMob[]>();

        foreach (var property in value.EnumerateObject())
        {
            var categoryPath = Field(path, property.Name);
            if (!mobCategories.Contains(property.Name))
                throw Error(categoryPath, $"isn't a mob category ({string.Join(", ", mobCategories)})");

            Array(property.Value, categoryPath);

            var mobs = new List<SpawnerMob>();
            var totalWeight = 0L;

            foreach (var item in property.Value.EnumerateArray())
            {
                var mob = ReadSpawner(item, Index(categoryPath, mobs.Count), requireCount: true);
                totalWeight += mob.Weight;
                mobs.Add(mob);
            }

            // Vanilla's WeightedList can't hold more than an int's worth of weight.
            if (totalWeight > int.MaxValue)
                throw Error(categoryPath, "the weights add up to more than 2147483647");

            spawners[property.Name] = [.. mobs];
        }

        return spawners;
    }

    /// <summary>
    /// Reads a 26.3 spawner's <c>count</c> provider. Obsidian's biome modifiers also accept their older bounds.
    /// </summary>
    public static SpawnerMob ReadSpawner(JsonElement value, string path, bool requireCount = false)
    {
        Object(value, path);

        var type = Identifier(Required(value, path, "type"), Field(path, "type"));
        if (!RegistryIds.IsEntityType(type))
            throw Error(Field(path, "type"), $"{type} isn't an entity type");

        var weight = Int(Required(value, path, "weight"), Field(path, "weight"), 0, int.MaxValue);

        if (requireCount || TryGet(value, "count", out _))
        {
            var count = IntProviderCodec.Read(Required(value, path, "count"), Field(path, "count"));

            return new SpawnerMob
            {
                Type = type,
                Count = count,
                MinCount = count.MinValue,
                MaxCount = count.MaxValue,
                Weight = weight
            };
        }

        var minCount = Int(Required(value, path, "minCount"), Field(path, "minCount"), 1, int.MaxValue);
        var maxCount = Int(Required(value, path, "maxCount"), Field(path, "maxCount"), 1, int.MaxValue);

        if (minCount > maxCount)
            throw Error(path, $"minCount ({minCount}) is more than maxCount ({maxCount})");

        return new SpawnerMob
        {
            Type = type,
            MinCount = minCount,
            MaxCount = maxCount,
            Weight = weight
        };
    }

    /// <summary>
    /// Reads spawn costs like vanilla's: <c>{energy_budget, charge}</c> by entity type identifier.
    /// </summary>
    public static Dictionary<string, SpawnCost> ReadSpawnCosts(JsonElement value, string path)
    {
        Object(value, path);

        var costs = new Dictionary<string, SpawnCost>();

        foreach (var property in value.EnumerateObject())
        {
            var costPath = Field(path, property.Name);
            if (!TryParseIdentifier(property.Name, out var type) || !RegistryIds.IsEntityType(type))
                throw Error(costPath, "isn't an entity type");

            var cost = Object(property.Value, costPath);

            costs[type] = new SpawnCost
            {
                EnergyBudget = Double(Required(cost, costPath, "energy_budget"), Field(costPath, "energy_budget")),
                Charge = Double(Required(cost, costPath, "charge"), Field(costPath, "charge"))
            };
        }

        return costs;
    }
}
