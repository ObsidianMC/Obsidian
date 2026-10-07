using Obsidian.API.Configuration;
using Obsidian.Nbt;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

/// <summary>
/// <c>level.dat</c> in vanilla 1.21.11's shape: a root compound holding the level in <c>Data</c>, with the keys of
/// vanilla's <c>PrimaryLevelData</c>. The names, tag types and nesting have to match vanilla's so the game reads the save.
/// </summary>
/// <remarks>
/// A world keeps the whole <c>Data</c> compound it was loaded with and only overwrites the fields Obsidian owns when it
/// saves (see <see cref="Update"/>), so what Obsidian doesn't model (the dragon fight, scheduled events, game rules...)
/// survives.
/// </remarks>
internal static class VanillaLevelData
{
    /// <summary>
    /// Vanilla 1.21.11's data version (<c>SharedConstants.WORLD_VERSION</c>).
    /// </summary>
    public const int DataVersion = 4671;

    private const string VersionName = "1.21.11";

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
    /// The <c>Data</c> compound of a vanilla-shaped root, or <c>null</c> for Obsidian's flat shape.
    /// </summary>
    public static NbtCompound? GetData(NbtCompound root) => root.TryGetTag<NbtCompound>("Data", out var data) ? data : null;

    /// <summary>
    /// Reads the fields Obsidian models.
    /// </summary>
    /// <remarks>
    /// Obsidian has one clock for game and day time, so it runs on <c>DayTime</c>, where the sun is; <see cref="Update"/>
    /// advances <c>Time</c> by as much as the clock moved.
    /// </remarks>
    public static LevelData Read(NbtCompound data)
    {
        var level = new LevelData
        {
            LevelName = Get(data, "LevelName", string.Empty)!,
            DefaultGamemode = (GameMode)Get(data, "GameType", 0),
            Hardcore = data.GetBool("hardcore"),
            AllowCommands = data.GetBool("allowCommands"),
            Difficulty = (Difficulty)Get<byte>(data, "Difficulty", (byte)Difficulty.Normal),
            DifficultyLocked = data.GetBool("DifficultyLocked"),
            Initialized = data.GetBool("initialized"),
            LastPlayed = Get(data, "LastPlayed", 0L),
            Time = Get(data, "DayTime", Get(data, "Time", 0L)),
            Raining = data.GetBool("raining"),
            Thundering = data.GetBool("thundering"),
            RainTime = Get(data, "rainTime", 0),
            ThunderTime = Get(data, "thunderTime", 0),
            ClearWeatherTime = Get(data, "clearWeatherTime", 0),
            Version = Get(data, "version", StorageVersion)
        };

        if (data.TryGetTag<NbtCompound>("WorldGenSettings", out var worldGen))
        {
            level.RandomSeed = Get(worldGen, "seed", 0L);
            level.MapFeatures = !worldGen.HasTag("generate_features") || worldGen.GetBool("generate_features");
        }

        // The spawn is a block; players spawn at its center. Saves from before 1.21.9 keep it in SpawnX/Y/Z.
        if (data.TryGetTag<NbtCompound>("spawn", out var spawn) && spawn.TryGetTag<NbtArray<int>>("pos", out var pos) && pos.Count == 3)
            level.SpawnPosition = new VectorD(pos[0] + 0.5, pos[1], pos[2] + 0.5);
        else if (data.HasTag("SpawnX"))
            level.SpawnPosition = new VectorD(Get(data, "SpawnX", 0) + 0.5, Get(data, "SpawnY", 0), Get(data, "SpawnZ", 0) + 0.5);

        return level;
    }

    /// <summary>
    /// Obsidian's generator for the overworld a level's <c>WorldGenSettings</c> describe.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Obsidian can't generate that overworld, including flat worlds with other layers or another biome than its superflat
    /// generator's: they'd silently get different terrain.
    /// </exception>
    public static string GetGeneratorId(NbtCompound data)
    {
        if (!data.TryGetTag<NbtCompound>("WorldGenSettings", out var worldGen)
            || !worldGen.TryGetTag<NbtCompound>("dimensions", out var dimensions)
            || !dimensions.TryGetTag<NbtCompound>(Overworld, out var overworld)
            || !overworld.TryGetTag<NbtCompound>("generator", out var generator))
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

        throw new NotSupportedException($"Obsidian can't generate this world's overworld ({type}, {settings ?? "custom settings"}).");
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
    /// The <c>Data</c> compound of a new level, as vanilla writes it for a world created with these settings. The fields
    /// Obsidian owns are filled in by <see cref="Update"/> when it's saved.
    /// </summary>
    public static NbtCompound Create(NewWorldConfiguration settings, string levelName, long seed, string generatorId)
    {
        var data = new NbtCompound("Data")
        {
            new NbtTag<string>("LevelName", levelName),
            new NbtTag<int>("GameType", (int)settings.GameMode),
            new NbtTag<byte>("Difficulty", (byte)settings.Difficulty),
            new NbtTag<byte>("hardcore", Byte(settings.Hardcore)),
            new NbtTag<byte>("allowCommands", Byte(settings.AllowCommands)),
            new NbtTag<int>("version", StorageVersion),
            new NbtTag<byte>("WasModded", 1),
            new NbtList(NbtTagType.String, "ServerBrands"),
            CreateWorldGenSettings(settings, seed, generatorId),
            new NbtCompound("DataPacks")
            {
                new NbtList(NbtTagType.String, "Enabled") { new NbtTag<string>(string.Empty, "vanilla") },
                new NbtList(NbtTagType.String, "Disabled")
            },
            new NbtList(NbtTagType.String, "enabled_features") { new NbtTag<string>(string.Empty, "minecraft:vanilla") }
        };

        // Vanilla 1.21.11 keeps game rules by registry id, booleans as bytes and integers as ints.
        var gameRules = new NbtCompound("game_rules");
        foreach (var (key, value) in settings.GameRules)
        {
            var id = key.Contains(':') ? key : $"minecraft:{key}";
            if (bool.TryParse(value, out var flag))
                gameRules.Add(new NbtTag<byte>(id, (byte)(flag ? 1 : 0)));
            else if (int.TryParse(value, out var number))
                gameRules.Add(new NbtTag<int>(id, number));
        }

        data.Add(gameRules);
        return data;
    }

    /// <summary>
    /// Overwrites the fields Obsidian owns in a level's <c>Data</c> with <paramref name="level"/>'s values, leaving the rest.
    /// </summary>
    public static void Update(NbtCompound data, LevelData level)
    {
        // Advances the game time by as much as the clock moved since the last save (see Read).
        var gameTime = Get(data, "Time", 0L) + Math.Max(0, level.Time - Get(data, "DayTime", level.Time));

        Set(data, new NbtTag<int>("DataVersion", DataVersion));
        Set(data, new NbtCompound("Version")
        {
            new NbtTag<int>("Id", DataVersion),
            new NbtTag<string>("Name", VersionName),
            new NbtTag<byte>("Snapshot", 0),
            new NbtTag<string>("Series", "main")
        });
        Set(data, new NbtTag<int>("GameType", (int)level.DefaultGamemode));
        Set(data, new NbtTag<byte>("hardcore", Byte(level.Hardcore)));
        Set(data, new NbtTag<byte>("allowCommands", Byte(level.AllowCommands)));
        Set(data, new NbtTag<byte>("Difficulty", (byte)level.Difficulty));
        Set(data, new NbtTag<byte>("DifficultyLocked", Byte(level.DifficultyLocked)));
        Set(data, new NbtTag<byte>("initialized", 1));
        Set(data, new NbtTag<long>("LastPlayed", DateTimeOffset.Now.ToUnixTimeMilliseconds()));
        Set(data, new NbtTag<long>("Time", gameTime));
        Set(data, new NbtTag<long>("DayTime", level.Time));
        Set(data, new NbtTag<byte>("raining", Byte(level.Raining)));
        Set(data, new NbtTag<byte>("thundering", Byte(level.Thundering)));
        Set(data, new NbtTag<int>("rainTime", level.RainTime));
        Set(data, new NbtTag<int>("thunderTime", level.ThunderTime));
        Set(data, new NbtTag<int>("clearWeatherTime", level.ClearWeatherTime));

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
    }

    /// <summary>
    /// Vanilla's <c>WorldGenSettings</c> for the world preset Obsidian generates: the overworld's generator, and vanilla's
    /// nether and end, which Obsidian generates in every world.
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

        return new NbtCompound("WorldGenSettings")
        {
            new NbtTag<long>("seed", seed),
            new NbtTag<byte>("generate_features", Byte(settings.GenerateStructures)),
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
