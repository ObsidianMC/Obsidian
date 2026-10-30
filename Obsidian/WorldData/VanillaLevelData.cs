using Obsidian.API.Configuration;
using Obsidian.Nbt;
using Obsidian.WorldData.Generators.Mojang;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// A world's level data in vanilla 26.3's shape (see <see cref="LevelFiles"/>): <c>level.dat</c>'s <c>Data</c> with the
/// keys of vanilla's <c>PrimaryLevelData</c>, and the game rules, weather, clocks and world generation settings in their
/// saved data files. The names, tag types and nesting have to match vanilla's so the game reads the save.
/// </summary>
/// <remarks>
/// A world keeps the whole compounds it was loaded with and only overwrites the fields Obsidian owns when it saves (see
/// <see cref="Update"/>), so what Obsidian doesn't model (paused clocks, data packs, rules it doesn't have...) survives.
/// </remarks>
internal static class VanillaLevelData
{
    // PrimaryLevelData's level format version, the "version" key.
    private const int StorageVersion = 19133;

    private const string Overworld = "minecraft:overworld";

    // Obsidian's generators for vanilla's default and flat overworlds.
    private const string MojangGeneratorId = "minecraft:mojang_generator";
    private const string SuperflatGeneratorId = "superflat";

    // The layers (bottom up) and biome of Obsidian's superflat generator, whose terrain is fixed.
    private const string FlatBiome = "minecraft:plains";
    private static readonly (string Block, int Height)[] FlatLayers =
    [
        ("minecraft:bedrock", 1),
        ("minecraft:dirt", 3),
        ("minecraft:grass_block", 1)
    ];

    /// <summary>
    /// Reads the fields Obsidian models, but the game rules (see <see cref="AbstractLevel.ReadGameRules"/>).
    /// </summary>
    /// <remarks>
    /// Obsidian has one clock for game and day time, so it runs on the overworld's world clock, where the sun is;
    /// <see cref="Update"/> advances <c>Time</c>, the game time, by as much as the clock moved.
    /// </remarks>
    public static LevelData Read(LevelFiles files)
    {
        var data = files.Data;
        var difficulty = data.TryGetTag<NbtCompound>("difficulty_settings", out var settings) ? settings : new NbtCompound();
        var weather = files.Weather;
        var worldGen = files.WorldGenSettings;

        var level = new LevelData
        {
            LevelName = Get(data, "LevelName", string.Empty)!,
            DefaultGamemode = (GameMode)Get(data, "GameType", 0),
            Hardcore = difficulty.GetBool("hardcore"),
            Difficulty = Enum.TryParse<Difficulty>(Get(difficulty, "difficulty", "normal"), ignoreCase: true, out var value)
                ? value
                : Difficulty.Normal,
            DifficultyLocked = difficulty.GetBool("locked"),
            AllowCommands = data.GetBool("allowCommands"),
            Initialized = data.GetBool("initialized"),
            LastPlayed = Get(data, "LastPlayed", 0L),
            Time = GetClock(files, Get(data, "Time", 0L)),
            Raining = weather.GetBool("raining"),
            Thundering = weather.GetBool("thundering"),
            RainTime = Get(weather, "rain_time", 0),
            ThunderTime = Get(weather, "thunder_time", 0),
            ClearWeatherTime = Get(weather, "clear_weather_time", 0),
            Version = Get(data, "version", StorageVersion),
            RandomSeed = Get(worldGen, "seed", 0L),
            MapFeatures = !worldGen.HasTag("generate_structures") || worldGen.GetBool("generate_structures")
        };

        // The spawn is a block; players spawn at its center.
        if (data.TryGetTag<NbtCompound>("spawn", out var spawn) && spawn.TryGetTag<NbtArray<int>>("pos", out var pos) && pos.Count == 3)
            level.SpawnPosition = new VectorD(pos[0] + 0.5, pos[1], pos[2] + 0.5);

        return level;
    }

    // The overworld's world clock, or the fallback when there's none.
    private static long GetClock(LevelFiles files, long fallback) =>
        files.WorldClocks.TryGetTag<NbtCompound>(Overworld, out var clock) ? Get(clock, "total_ticks", fallback) : fallback;

    /// <summary>
    /// The UUID of the singleplayer owner, whose player file the game loads for whoever opens the world in singleplayer,
    /// or <c>null</c> when it was never opened that way.
    /// </summary>
    public static Guid? GetSingleplayerOwner(LevelFiles files) =>
        files.Data.TryGetTag<NbtArray<int>>("singleplayer_uuid", out var uuid) && uuid.Count == 4
            ? Entities.EntityNbt.UuidFromInts(uuid.GetArray())
            : null;

    /// <summary>
    /// Records <paramref name="owner"/> as the singleplayer owner (see <see cref="GetSingleplayerOwner"/>).
    /// </summary>
    public static void SetSingleplayerOwner(LevelFiles files, Guid owner) =>
        Set(files.Data, new NbtArray<int>("singleplayer_uuid", Entities.EntityNbt.UuidToInts(owner)));

    /// <summary>
    /// Obsidian's generator for the overworld a world's generation settings describe.
    /// </summary>
    /// <remarks>
    /// A world made in vanilla with a data pack changing the overworld, like Terralith, saves that pack's level stem.
    /// Vanilla reads the pack's stem over the saved one on every load, so such a world opens when a data pack or
    /// plugin overrides the overworld's stem with the same generator (see <see cref="SavedLevelStem"/>).
    /// </remarks>
    /// <param name="overworld">The file overriding the overworld's level stem, if any: where it's from, for
    /// messages, and its JSON.</param>
    /// <exception cref="NotSupportedException">
    /// Obsidian can't generate that overworld, including flat worlds with other layers or another biome than its superflat
    /// generator's: they'd silently get different terrain.
    /// </exception>
    public static string GetGeneratorId(LevelFiles files, (string Source, byte[] Stem)? overworld = null)
    {
        if (!files.WorldGenSettings.TryGetTag<NbtCompound>("dimensions", out var dimensions)
            || !dimensions.TryGetTag<NbtCompound>(Overworld, out var savedStem)
            || !savedStem.TryGetTag<NbtCompound>("generator", out var generator))
            return MojangGeneratorId;

        var type = Get<string?>(generator, "type", null);
        if (type == "minecraft:flat")
        {
            if (!HasSuperflatTerrain(generator))
            {
                throw new NotSupportedException("Obsidian can only open flat worlds with its own layers "
                    + "(bedrock, 3 dirt, grass block, in plains); this one has other layers or another biome.");
            }

            return SuperflatGeneratorId;
        }

        // Vanilla's default overworld: noise with the overworld's settings and multi-noise biomes. Large biomes and
        // amplified differ in their settings, single biome in its biome source.
        var settings = Get<string?>(generator, "settings", null);
        var biomeSource = generator.TryGetTag<NbtCompound>("biome_source", out var source) ? source : new NbtCompound();
        var isMultiNoise = Get<string?>(biomeSource, "type", null) == "minecraft:multi_noise";
        var biomePreset = Get<string?>(biomeSource, "preset", null);

        if (type == "minecraft:noise" && settings == Overworld && isMultiNoise && biomePreset == Overworld)
            return MojangGeneratorId;

        var described = $"{type}, {settings ?? "custom settings"}, {biomePreset ?? "custom biomes"}";
        if (type != "minecraft:noise")
            throw new NotSupportedException($"Obsidian can't generate this world's overworld ({described}).");

        if (overworld is not (string stemSource, byte[] stem))
        {
            throw new NotSupportedException(
                $"This world's overworld ({described}) isn't vanilla's default. Obsidian generates it only with a "
                + "data pack or plugin overriding the minecraft:overworld level stem with the same generator, like "
                + "the data pack the world was made with in the data packs folder.");
        }

        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        using var document = JsonDocument.Parse(stem, options);

        if (SavedLevelStem.FindDifference(savedStem, document.RootElement) is string difference)
        {
            throw new NotSupportedException(
                $"This world's overworld was made with a data pack other than {stemSource}, whose "
                + $"minecraft:overworld level stem differs at {(difference.Length == 0 ? "its root" : difference)}. "
                + "Use the data pack the world was made with.");
        }

        return MojangGeneratorId;
    }

    /// <summary>
    /// Whether a flat generator's settings describe the terrain Obsidian's superflat generator makes: the same layers and
    /// biome (vanilla's default when the biome is missing is plains).
    /// </summary>
    private static bool HasSuperflatTerrain(NbtCompound generator)
    {
        if (!generator.TryGetTag<NbtCompound>("settings", out var settings)
            || !settings.TryGetTag<NbtList>("layers", out var layers)
            || Get(settings, "biome", FlatBiome) != FlatBiome)
            return false;

        var savedLayers = layers
            .OfType<NbtCompound>()
            .Select(layer => (Block: Get(layer, "block", string.Empty), Height: Get(layer, "height", 0)));

        return savedLayers.SequenceEqual(FlatLayers);
    }

    /// <summary>
    /// The id of Obsidian's generator for a vanilla world type (<c>NewWorld:WorldType</c>).
    /// </summary>
    /// <exception cref="NotSupportedException">Obsidian can't generate that world type.</exception>
    public static string GetGeneratorId(string worldType) => worldType.ToLowerInvariant().Replace("minecraft:", string.Empty) switch
    {
        "" or "default" or "normal" => MojangGeneratorId,
        "flat" => SuperflatGeneratorId,
        _ => throw new NotSupportedException($"Obsidian can't generate the world type '{worldType}' yet; use default or flat.")
    };

    /// <summary>
    /// A new level's seed, by vanilla's rule: none picks a random one, a number is used as-is, other text is hashed.
    /// </summary>
    public static long ParseSeed(string? seed) => string.IsNullOrWhiteSpace(seed) ? Random.Shared.NextInt64() : RandomState.ParseSeed(seed);

    /// <summary>
    /// The level data of a new world, as vanilla writes it for a world created with these settings. The fields Obsidian
    /// owns are filled in by <see cref="Update"/> when it's saved.
    /// </summary>
    public static LevelFiles Create(NewWorldConfiguration settings, string levelName, long seed, string generatorId)
    {
        var files = new LevelFiles
        {
            Data = new NbtCompound("Data")
            {
                new NbtTag<string>("LevelName", levelName),
                new NbtTag<int>("GameType", (int)settings.GameMode),
                new NbtCompound("difficulty_settings")
                {
                    new NbtTag<string>("difficulty", DifficultyName(settings.Difficulty)),
                    new NbtTag<byte>("hardcore", Byte(settings.Hardcore)),
                    new NbtTag<byte>("locked", 0)
                },
                new NbtTag<byte>("allowCommands", Byte(settings.AllowCommands)),
                new NbtTag<int>("version", StorageVersion),
                new NbtTag<byte>("WasModded", 1),
                new NbtList(NbtTagType.String, "ServerBrands"),
                new NbtCompound("DataPacks")
                {
                    new NbtList(NbtTagType.String, "Enabled") { new NbtTag<string>(string.Empty, "vanilla") },
                    new NbtList(NbtTagType.String, "Disabled")
                }
            },
            WorldGenSettings = CreateWorldGenSettings(settings, seed, generatorId)
        };

        SetGameRules(files.GameRules, settings.GameRules);
        return files;
    }

    /// <summary>
    /// Sets <paramref name="rules"/> in the payload of <c>game_rules.dat</c>, which keeps them by registry id, booleans as
    /// bytes and integers as ints.
    /// </summary>
    public static void SetGameRules(NbtCompound gameRules, IEnumerable<KeyValuePair<string, string>> rules)
    {
        foreach (var (key, value) in rules)
        {
            var id = key.Contains(':') ? key : $"minecraft:{key}";
            if (bool.TryParse(value, out var flag))
                Set(gameRules, new NbtTag<byte>(id, (byte)(flag ? 1 : 0)));
            else if (int.TryParse(value, out var number))
                Set(gameRules, new NbtTag<int>(id, number));
        }
    }

    /// <summary>
    /// Overwrites the fields Obsidian owns in a world's level data with <paramref name="level"/>'s values, leaving the rest.
    /// </summary>
    public static void Update(LevelFiles files, LevelData level)
    {
        var data = files.Data;

        // Advances the game time by as much as the clock moved since the last save (see Read).
        var gameTime = Get(data, "Time", 0L) + Math.Max(0, level.Time - GetClock(files, level.Time));

        Set(data, new NbtTag<int>("DataVersion", ServerConstants.DataVersion));
        Set(data, new NbtCompound("Version")
        {
            new NbtTag<int>("Id", ServerConstants.DataVersion),
            new NbtTag<string>("Name", ServerConstants.ProtocolDescription),
            new NbtTag<byte>("Snapshot", 0),
            new NbtTag<string>("Series", "main")
        });

        // Vanilla adds every data version that saved the world.
        if (!data.TryGetTag<NbtList>("version_history", out var history))
            Set(data, history = new NbtList(NbtTagType.Int, "version_history"));
        if (history.OfType<NbtTag<int>>().LastOrDefault()?.Value != ServerConstants.DataVersion)
            history.Add(new NbtTag<int>(string.Empty, ServerConstants.DataVersion));

        Set(data, new NbtTag<int>("GameType", (int)level.DefaultGamemode));
        Set(data, new NbtTag<byte>("allowCommands", Byte(level.AllowCommands)));
        Set(data, new NbtTag<byte>("initialized", 1));
        Set(data, new NbtTag<long>("LastPlayed", DateTimeOffset.Now.ToUnixTimeMilliseconds()));
        Set(data, new NbtTag<long>("Time", gameTime));

        var difficulty = data.TryGetTag<NbtCompound>("difficulty_settings", out var savedDifficulty)
            ? savedDifficulty
            : new NbtCompound("difficulty_settings");
        Set(difficulty, new NbtTag<string>("difficulty", DifficultyName(level.Difficulty)));
        Set(difficulty, new NbtTag<byte>("hardcore", Byte(level.Hardcore)));
        Set(difficulty, new NbtTag<byte>("locked", Byte(level.DifficultyLocked)));
        Set(data, difficulty);

        // Vanilla's RespawnData: a dimension, a block position and the facing, which Obsidian keeps as loaded.
        var spawn = level.SpawnPosition.Floor();
        var spawnTag = data.TryGetTag<NbtCompound>("spawn", out var existing) ? existing : new NbtCompound("spawn")
        {
            new NbtTag<float>("yaw", 0f),
            new NbtTag<float>("pitch", 0f)
        };
        Set(spawnTag, new NbtTag<string>("dimension", Overworld));
        Set(spawnTag, new NbtArray<int>("pos", [(int)spawn.X, (int)spawn.Y, (int)spawn.Z]));
        Set(data, spawnTag);

        // Vanilla adds the brand of every server that ran the world.
        if (!data.TryGetTag<NbtList>("ServerBrands", out var brands))
            Set(data, brands = new NbtList(NbtTagType.String, "ServerBrands"));
        if (!brands.OfType<NbtTag<string>>().Any(brand => brand.Value == "obsidian"))
            brands.Add(new NbtTag<string>(string.Empty, "obsidian"));
        Set(data, new NbtTag<byte>("WasModded", 1));

        Set(files.Weather, new NbtTag<byte>("raining", Byte(level.Raining)));
        Set(files.Weather, new NbtTag<byte>("thundering", Byte(level.Thundering)));
        Set(files.Weather, new NbtTag<int>("rain_time", level.RainTime));
        Set(files.Weather, new NbtTag<int>("thunder_time", level.ThunderTime));
        Set(files.Weather, new NbtTag<int>("clear_weather_time", level.ClearWeatherTime));

        var clock = files.WorldClocks.TryGetTag<NbtCompound>(Overworld, out var savedClock) ? savedClock : new NbtCompound(Overworld);
        Set(clock, new NbtTag<long>("total_ticks", level.Time));
        Set(files.WorldClocks, clock);

        // Rules Obsidian doesn't have stay as they were.
        SetGameRules(files.GameRules, level.GameRules);
    }

    // Vanilla's Difficulty by its serialized name, like "normal".
    private static string DifficultyName(Difficulty difficulty) => difficulty.ToString().ToLowerInvariant();

    /// <summary>
    /// The payload of vanilla's <c>world_gen_settings.dat</c> for the world preset Obsidian generates: the overworld's
    /// generator, and vanilla's nether and end, which Obsidian generates in every world. A world with a plugin's generator
    /// is described as vanilla's default overworld, which is what vanilla would generate for it.
    /// </summary>
    private static NbtCompound CreateWorldGenSettings(NewWorldConfiguration settings, long seed, string generatorId)
    {
        var overworldGenerator = generatorId == SuperflatGeneratorId
            // Obsidian's superflat layers, which FlatPreset doesn't change.
            ? new NbtCompound("generator")
            {
                new NbtTag<string>("type", "minecraft:flat"),
                new NbtCompound("settings")
                {
                    new NbtTag<string>("biome", FlatBiome),
                    new NbtTag<byte>("features", 0),
                    new NbtTag<byte>("lakes", 0),
                    FlatLayerList()
                }
            }
            : NoiseGenerator(Overworld, MultiNoise(Overworld));

        return new NbtCompound("data")
        {
            new NbtTag<long>("seed", seed),
            new NbtTag<byte>("generate_structures", Byte(settings.GenerateStructures)),
            new NbtTag<byte>("bonus_chest", Byte(settings.BonusChest)),
            new NbtCompound("dimensions")
            {
                Dimension(Overworld, overworldGenerator),
                Dimension("minecraft:the_nether", NoiseGenerator("minecraft:nether", MultiNoise("minecraft:nether"))),
                Dimension("minecraft:the_end", NoiseGenerator("minecraft:end", new NbtCompound("biome_source")
                {
                    new NbtTag<string>("type", "minecraft:the_end")
                }))
            }
        };

        static NbtList FlatLayerList()
        {
            var list = new NbtList(NbtTagType.Compound, "layers");
            foreach (var (block, height) in FlatLayers)
            {
                list.Add(new NbtCompound
                {
                    new NbtTag<string>("block", block),
                    new NbtTag<int>("height", height)
                });
            }

            return list;
        }

        static NbtCompound Dimension(string name, NbtCompound generator) => new(name)
        {
            new NbtTag<string>("type", name),
            generator
        };

        static NbtCompound NoiseGenerator(string settings, NbtCompound biomeSource) => new("generator")
        {
            new NbtTag<string>("type", "minecraft:noise"),
            new NbtTag<string>("settings", settings),
            biomeSource
        };

        static NbtCompound MultiNoise(string preset) => new("biome_source")
        {
            new NbtTag<string>("type", "minecraft:multi_noise"),
            new NbtTag<string>("preset", preset)
        };
    }

    private static byte Byte(bool value) => (byte)(value ? 1 : 0);

    private static T Get<T>(NbtCompound compound, string name, T fallback) =>
        compound.TryGetTagValue<T>(name, out var value) ? value : fallback;

    private static void Set(NbtCompound compound, INbtTag tag)
    {
        compound.Remove(tag.Name!);
        compound.Add(tag);
    }
}
