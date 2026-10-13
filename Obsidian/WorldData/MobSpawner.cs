using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.Providers.IntProviders;
using Obsidian.WorldData.Generators;
using System.Reflection;
using System.Text.Json;
using System.IO;

namespace Obsidian.WorldData;

internal sealed partial class MobSpawner(AbstractLevel level)
{
    private static readonly SpawnerMob[] fortressSpawns = LoadFortressSpawns();
    private static readonly (string Name, int Cap, bool Rare)[] spawnCategories =
    [("monster", 70, false), ("creature", 10, true), ("ambient", 15, false), ("axolotls", 5, false),
        ("underground_water_creature", 5, false), ("water_creature", 5, false), ("water_ambient", 20, false)];

    // Vanilla's entity types' mob categories (EntityType.getCategory), from Assets/entities.json. Vanilla counts a mob
    // against its type's category's cap, whichever biome list it spawned from (jungles list ocelots as monsters).
    private static readonly Dictionary<string, string> mobCategories = LoadMobCategories();

    /// <summary>
    /// The category a mob counts in: a plugin's mob in its type's, a vanilla one like
    /// <see cref="SpawnCategory(EntityType?)"/>.
    /// </summary>
    internal static string SpawnCategory(Mob mob) => mob.Definition is API.Entities.EntityDefinition definition
        ? PluginSpawns.Name(definition.Settings.Category)
        : SpawnCategory(mob.Type);

    /// <summary>
    /// A vanilla entity type's mob category, like <c>monster</c>; <c>misc</c> for a plugin's mob (null).
    /// </summary>
    internal static string SpawnCategory(EntityType? type) => type is EntityType vanilla
        ? mobCategories.GetValueOrDefault(EntityNbt.TypeId(vanilla), "misc")
        : "misc";

    private static Dictionary<string, string> LoadMobCategories()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.entities.json")
            ?? throw new InvalidDataException("Missing entity type data.");
        using var document = JsonDocument.Parse(stream);

        // Entries without a category are the entity classes' abstract superclasses.
        return document.RootElement
            .EnumerateObject()
            .Where(type => type.Value.TryGetProperty("category", out _))
            .ToDictionary(type => type.Name, type => type.Value.GetProperty("category").GetString()!);
    }

    internal static bool IsAquatic(EntityType type) => type is EntityType.Squid or EntityType.GlowSquid or EntityType.Dolphin or EntityType.Axolotl or EntityType.Tadpole or
        EntityType.Cod or EntityType.Salmon or EntityType.TropicalFish or EntityType.Pufferfish or EntityType.Drowned or EntityType.Guardian or EntityType.ElderGuardian;
    private static SpawnerMob[] LoadFortressSpawns()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.worldgen.structures.nether.json")
            ?? throw new InvalidDataException("Missing Nether structure spawn data.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("minecraft:fortress").GetProperty("spawn_overrides").GetProperty("monster")
            .GetProperty("spawns").Deserialize<SpawnerMob[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    private readonly Random random = new();

    // The darkness checks' draws, from a random like vanilla's level's so that the dimension type's light level
    // providers sample as vanilla's do.
    private readonly LegacyRandomSource lightRandom = new(Random.Shared.NextInt64());
    private readonly Dictionary<string, bool> supportedSpawns = [];
    private bool SupportsSpawn(string id)
    {
        // A plugin's mobs spawn while their plugin is loaded, unless they're misc: plugin mobs don't despawn, and misc
        // ones would never count against a cap.
        if (!id.StartsWith("minecraft:", StringComparison.Ordinal)
            && EntityDefinitionsRegistry.TryGet(id, out var definition))
        {
            return definition.Kind == API.Entities.EntityKind.Mob
                && definition.Binding is not null
                && definition.Settings.Category != API.Entities.MobCategory.Misc;
        }

        if (!supportedSpawns.TryGetValue(id, out var supported))
            supportedSpawns[id] = supported = EntityNbt.TryParseType(id, out var type) && EntitySpawner.CreateMob(level, type) is { HasAi: true };
        return supported;
    }
    private long ticks;
    private int phantomDelay;

    internal void PopulateChunk(IChunk chunk)
    {
        if (level.Generator is MobTestGenerator || !level.LevelData.GetBooleanRule("spawn_mobs")) return;

        // Like vanilla, the chance of each further group is that of the biome at the chunk's corner at the top.
        var biome = chunk.GetBiome(chunk.X * 16, level.MinY + level.Height - 1, chunk.Z * 16);
        var probability = PluginSpawns.Current.CreatureProbabilityOf(biome.Name);

        while (random.NextSingle() < probability)
            SpawnGroup(chunk, "creature", [], 32, true);
    }

    internal void Tick()
    {
        if (level.Generator is MobTestGenerator) return;
        ticks++;
        var players = level.Players.Values.Where(player => player.GameMode != GameMode.Spectator).ToArray();
        if (players.Length == 0)
            return;
        var chunks = new HashSet<long>();
        foreach (var player in players)
        {
            var (cx, cz) = Region.ChunkOf(player.Position);
            var distance = Math.Min(8, (int)level.Configuration.SimulationDistance);
            for (var x = cx - distance; x <= cx + distance; x++)
            for (var z = cz - distance; z <= cz + distance; z++)
                if (level.GetLoadedChunk(x, z) != null &&
                    (new VectorD(x * 16 + 8, player.Position.Y, z * 16 + 8) - player.Position).MagnitudeSquared() <= 16384)
                    chunks.Add(NumericsHelper.IntsToLong(x, z));
        }
        var mobs = level.Regions.Values.SelectMany(region => region.Entities.Values).OfType<Mob>()
            .Where(mob => mob.Alive && level.IsMobTicking(mob.Position)).ToArray();
        var counts = mobs.Where(mob => !mob.PersistenceRequired && mob.CustomName == null)
            .GroupBy(SpawnCategory).ToDictionary(group => group.Key, group => group.Count());
        var potential = this.Potential();
        foreach (var packed in chunks.OrderBy(_ => random.Next()))
        {
            NumericsHelper.LongToInts(packed, out var cx, out var cz);
            var chunk = level.GetLoadedChunk(cx, cz)!;
            foreach (var blockEntity in chunk.GetBlockEntities().OfType<DataBlockEntity>().ToArray())
            {
                if (blockEntity.Id == "minecraft:mob_spawner") TickSpawner(blockEntity, players);
                else if (blockEntity.Id == "minecraft:beehive") TickHive(blockEntity);
            }
            if (chunk is Chunk concrete) TickFrogspawn(concrete);
            TickThunder(chunk);
            foreach (var category in spawnCategories)
            {
                if (!level.LevelData.GetBooleanRule("spawn_mobs") || category.Rare && ticks % 400 != 0 ||
                    category.Name == "monster" && (level.LevelData.Difficulty == Difficulty.Peaceful || !level.LevelData.GetBooleanRule("spawn_monsters")))
                    continue;
                var cap = category.Cap * chunks.Count / 289;
                var count = counts.GetValueOrDefault(category.Name);
                if (count < cap)
                    SpawnGroup(chunk, category.Name, players, cap - count, counts: counts, potential: potential);
            }
        }
        if (level.LevelData.GetBooleanRule("spawn_mobs"))
        {
            TickPhantoms(players);
            if (ticks % 1200 == 0) SpawnVillageCats(mobs);
        }
    }

    // The charges of the loaded mobs that may despawn, whose biomes give their types spawn costs, like vanilla's
    // NaturalSpawner.createState, which takes every loaded entity, ticking or not.
    private SpawnPotential Potential()
    {
        var spawns = PluginSpawns.Current;
        var potential = new SpawnPotential();
        var mobs = level.Regions.Values.SelectMany(region => region.Entities.Values).OfType<Mob>();

        foreach (var mob in mobs)
        {
            if (!mob.Alive
                || mob.PersistenceRequired
                || mob.CustomName is not null
                || SpawnCategory(mob) == "misc")
                continue;

            var position = (Vector)mob.Position;

            if (level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not IChunk chunk)
                continue;

            var y = Math.Clamp(position.Y, level.MinY, level.MinY + level.Height - 1);
            var biome = chunk.GetBiome(position.X, y, position.Z);
            var type = EntityNbt.TypeId(mob);

            if (spawns.CostOf(biome.Name, type) is SpawnCost cost)
                potential.Add(position, cost.Charge);
        }

        return potential;
    }

    // Counts, when given, each spawned mob against its own type's category, like vanilla's SpawnState.afterSpawn:
    // a mob a biome lists under another category (jungles' ocelots, listed as monsters) still counts as its own.
    // With a potential, mobs whose biome gives their type a spawn cost spawn only within its energy budget.
    private int SpawnGroup(
        IChunk chunk,
        string category,
        IPlayer[] players,
        int remaining,
        bool generation = false,
        Dictionary<string, int>? counts = null,
        SpawnPotential? potential = null)
    {
        var terrain = new MobTerrain(level);
        var x = chunk.X * 16 + random.Next(16);
        var z = chunk.Z * 16 + random.Next(16);
        var top = GetSpawnHeight(chunk, x, z);
        var maxY = level.MinY + level.Height;
        var seaLevel = level.Generator is MojangGenerator generator ? generator.SeaLevel : 63;
        var y = category == "creature" ? top : category == "water_creature" ? random.Next(seaLevel - 23, seaLevel) :
            random.Next(level.MinY, Math.Clamp(top + 1, level.MinY + 1, maxY));
        var biome = chunk.GetBiome(x, Math.Clamp(y, level.MinY, maxY - 1), z);
        // Vanilla's biomes' spawners, with plugins' biomes and spawn modifiers.
        if (!PluginSpawns.Current.Biomes.TryGetValue(biome.Name, out var categories)
            || !categories.TryGetValue(category, out var choices)
            || choices.Length == 0)
        {
            return 0;
        }

        if (category == "monster" && level.Generator is MojangGenerator structureGenerator &&
            structureGenerator.IsInsideFortress(new Vector(x, y, z)))
            choices = fortressSpawns;

        // Vanilla's swamps' bogged and desert's parched come from their biome data, so modifiers can remove them.
        choices = choices.Where(entry => SupportsSpawn(entry.Type)).ToArray();
        var total = choices.Sum(choice => choice.Weight);
        if (total <= 0)
            return 0;
        var selected = random.Next(total);
        SpawnerMob? choice = null;
        foreach (var candidate in choices)
        {
            selected -= candidate.Weight;
            if (selected < 0)
            {
                choice = candidate;
                break;
            }
        }
        if (choice == null)
            return 0;

        // A vanilla mob, or a plugin's (SupportsSpawn checked it can spawn), which follows the rules of the category it
        // was chosen in, as a vanilla mob without rules of its own would.
        EntityType? type = null;
        API.Entities.EntityDefinition? definition = null;

        if (EntityNbt.TryParseType(choice.Type, out var vanillaType))
            type = vanillaType;
        else if (!EntityDefinitionsRegistry.TryGet(choice.Type, out definition))
            return 0;

        var aquatic = type is EntityType known
            ? IsAquatic(known)
            : category is "axolotls" or "underground_water_creature" or "water_creature" or "water_ambient";

        var spawned = 0;
        int? wolfVariant = null;
        var group = type == EntityType.Ghast ? 1 : random.Next(choice.MinCount, choice.MaxCount + 1);
        for (var attempt = 0; attempt < group * 4 && spawned < group && spawned < remaining; attempt++)
        {
            x += random.Next(6) - random.Next(6);
            z += random.Next(6) - random.Next(6);
            if (generation)
            {
                x = Math.Clamp(x, chunk.X * 16, chunk.X * 16 + 15);
                z = Math.Clamp(z, chunk.Z * 16, chunk.Z * 16 + 15);
            }
            if (level.GetLoadedChunk(x >> 4, z >> 4) is not { } candidateChunk)
                continue;
            if (generation)
                y = GetSpawnHeight(candidateChunk, x, z);
            var position = new VectorD(x + 0.5f, y, z + 0.5f);
            if (!generation && (players.Any(player => (player.Position - position).MagnitudeSquared() < 576) ||
                !players.Any(player => (player.Position - position).MagnitudeSquared() <= 16384) ||
                (level.LevelData.SpawnPosition - position).MagnitudeSquared() < 576))
                continue;
            if (type == EntityType.Blaze && (level.Generator is not MojangGenerator fortressGenerator ||
                !fortressGenerator.IsInsideFortress(new Vector(x, y, z))))
                continue;
            var mob = definition is null
                ? EntitySpawner.CreateMob(level, type)
                : PluginEntity.Create(definition, level) as Mob;
            if (mob == null)
                continue;
            var feet = terrain.GetBlock(new Vector(x, y, z));
            var floor = terrain.GetBlock(new Vector(x, y - 1, z));
            if (feet == null || floor == null || !terrain.IsFree(mob.Dimension.CreateBBFromPosition(position)) ||
                (aquatic ? !feet.Is(BlockIds.Water) :
                    feet.IsLiquid || type is not EntityType.Ghast and not EntityType.Bat && (floor.IsLiquid || BlockCollisionShapes.Get(floor).Count == 0)))
                continue;
            var sky = candidateChunk.GetLightLevel(x, y, z, LightType.Sky);
            var block = candidateChunk.GetLightLevel(x, y, z, LightType.Block);
            if (category == "creature")
            {
                if ((type switch
                    {
                        EntityType.Mooshroom => !floor.Is(BlockIds.Mycelium),
                        EntityType.Wolf => !TagsRegistry.Block.WolvesSpawnableOn.Entries.Contains(floor.RegistryId),
                        EntityType.Goat => !TagsRegistry.Block.GoatsSpawnableOn.Entries.Contains(floor.RegistryId),
                        EntityType.Rabbit => !TagsRegistry.Block.RabbitsSpawnableOn.Entries.Contains(floor.RegistryId),
                        EntityType.PolarBear => TagsRegistry.Worldgen.Biome.PolarBearsSpawnOnAlternateBlocks.Entries.Contains(candidateChunk.GetBiome(x, y, z).Id)
                            ? !TagsRegistry.Block.PolarBearsSpawnableOnAlternate.Entries.Contains(floor.RegistryId)
                            : !TagsRegistry.Block.AnimalsSpawnableOn.Entries.Contains(floor.RegistryId),
                        EntityType.Frog => !TagsRegistry.Block.FrogsSpawnableOn.Entries.Contains(floor.RegistryId),
                        EntityType.Parrot => !TagsRegistry.Block.ParrotsSpawnableOn.Entries.Contains(floor.RegistryId),
                        EntityType.Camel => !floor.Is(BlockIds.Sand),
                        EntityType.Ocelot => !floor.Is(BlockIds.GrassBlock)
                            && !TagsRegistry.Block.Leaves.Entries.Contains(floor.RegistryId),
                        _ => !floor.Is(BlockIds.GrassBlock)
                    }) ||
                    Math.Max(block, Math.Max(0, sky - (!generation && level.DayTime is >= 12000 and < 23000 ? 11 : 0))) <= 8)
                    continue;
            }
            else if (aquatic)
            {
                if (terrain.GetBlock(new Vector(x, y + 1, z))?.RegistryId != BlockIds.Water)
                    continue;
                if (type == EntityType.Drowned && !this.IsDarkEnoughToSpawn(block, sky))
                    continue;
                if (type is EntityType.Squid or EntityType.Dolphin && (y <= seaLevel - 24 || y >= seaLevel - 1) ||
                    type == EntityType.GlowSquid && (y >= seaLevel - 33 || Math.Max(block, sky) > 0))
                    continue;
                if (type == EntityType.Axolotl
                    && !Enumerable.Range(1, 5).Any(depth =>
                        terrain.GetBlock(new Vector(x, y - depth, z))?.RegistryId == BlockIds.Clay))
                    continue;
            }
            else if (type == EntityType.Bat)
            {
                var date = DateTime.Now;
                var halloween = date.Month == 10 && date.Day >= 20 || date.Month == 11 && date.Day <= 3;
                if (y >= seaLevel || !halloween && random.Next(2) == 0 || Math.Max(block, sky) > random.Next(halloween ? 7 : 4))
                    continue;
            }
            else if (type == EntityType.Ghast)
            {
                // Vanilla's ghast rule: one try in 20, at any light.
                if (random.Next(20) != 0)
                    continue;
            }
            else if (type == EntityType.Slime)
            {
                var localBiome = candidateChunk.GetBiome(x, y, z);
                ReadOnlySpan<float> phases = [1, 0.75f, 0.5f, 0.25f, 0, 0.25f, 0.5f, 0.75f];
                var moon = phases[(int)(level.LevelData.Time / 24000 % 8 + 8) % 8];
                var swamp = localBiome.Name == "minecraft:swamp" && y is > 50 and < 70 && random.NextSingle() < moon &&
                    Math.Max(block, Math.Max(0, sky - level.SkyDarken())) <= random.Next(8);
                if (!swamp && !(y < 40 && IsSlimeChunk(level.LevelData.RandomSeed, x >> 4, z >> 4) && random.Next(10) == 0))
                    continue;
            }
            else if (!IgnoresDarkness(type) && !this.IsDarkEnoughToSpawn(block, sky))
                continue;
            if (type is EntityType.Husk or EntityType.Parched or EntityType.Stray && sky != 15)
                continue;

            var cost = potential is null
                ? null
                : PluginSpawns.Current.CostOf(candidateChunk.GetBiome(x, y, z).Name, choice.Type);

            if (cost is not null && !potential!.Allows(new Vector(x, y, z), cost))
                continue;

            mob.EntityId = Server.GetNextEntityId();
            mob.Position = position;
            mob.Yaw = random.Next(360);
            mob.InitializeAi();
            if (mob is Wolf wolf)
            {
                wolfVariant ??= wolf.Variant;
                wolf.Variant = wolfVariant.Value;
            }
            if (mob is AgeableMob animal && mob is not Frog and not Parrot && spawned > 0 && random.NextSingle() < 0.05f)
                animal.IsBaby = true;
            if (!terrain.IsFree(mob.Dimension.CreateBBFromPosition(position)) || level.GetEntitiesInRange(position, 2)
                .Any(entity => MobTerrain.Overlaps(mob.Dimension.CreateBBFromPosition(position), entity.Dimension.CreateBBFromPosition(entity.Position))))
                continue;

            // A plugin's mob doesn't spawn once its plugin started unloading.
            if (!level.TrySpawnEntity(mob))
                continue;

            spawned++;

            if (cost is not null)
                potential!.Add(new Vector(x, y, z), cost.Charge);

            if (counts is not null)
            {
                var counted = SpawnCategory(mob);
                counts[counted] = counts.GetValueOrDefault(counted) + 1;
            }
        }
        return spawned;
    }

    private static int GetSpawnHeight(IChunk chunk, int x, int z)
    {
        var y = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(x & 15, z & 15);
        // Legacy generators store the occupied surface Y; newer ones store the first free Y.
        if (y < chunk.MinY + chunk.Height && BlockCollisionShapes.Get(chunk.GetBlock(x, y, z)).Count > 0)
            y++;
        return y;
    }

    // Mobs whose vanilla spawn rules don't ask for darkness (Monster.checkAnyLightMonsterSpawnRules, or their own
    // rules), so the dimension type's monster light rules don't apply to them.
    private static bool IgnoresDarkness(EntityType? type) =>
        type is EntityType.Blaze
            or EntityType.MagmaCube
            or EntityType.Silverfish
            or EntityType.ZombifiedPiglin
            or EntityType.Piglin
            or EntityType.Hoglin;

    /// <summary>
    /// Vanilla's <c>Monster.isDarkEnoughToSpawn</c> with the level's dimension type: sky light against a roll of 32,
    /// block light against the type's block light limit, then the light against a sample of its monster spawn light
    /// level. The limits are vanilla's per dimension type, so a plugin's dimension spawns monsters like its type.
    /// </summary>
    private bool IsDarkEnoughToSpawn(int block, int sky)
    {
        if (sky > lightRandom.NextInt(32))
            return false;

        var type = level.DimensionType.Element;
        if (type.MonsterSpawnBlockLightLimit < 15 && block > type.MonsterSpawnBlockLightLimit)
            return false;

        // Vanilla darkens the sky by 10 while it thunders, whatever the time, and otherwise by the level's darkening.
        var darkening = level.IsThundering() ? 10 : level.SkyDarken();
        var light = Math.Max(block, Math.Max(0, sky - darkening));

        // Drawn last, as vanilla does.
        return light <= MonsterSpawnLightTest(type).Sample(lightRandom);
    }

    // The provider of the type's monster spawn light level: a plugin's type's decoded provider, or one made from the
    // constant or range that vanilla's types (constant or uniform) and types made in code keep.
    private static IIntProvider MonsterSpawnLightTest(DimensionElement type)
    {
        if (type.MonsterSpawnLightProvider is not null)
            return type.MonsterSpawnLightProvider;

        if (type.MonsterSpawnLightLevel.Value is not { } range)
            return new ConstantIntProvider { Value = type.MonsterSpawnLightLevel.IntValue ?? 0 };

        return range.Type == IntProviderTypes.BiasedToBottom
            ? new BiasedToBottomIntProvider { MinInclusive = range.MinInclusive, MaxInclusive = range.MaxInclusive }
            : new UniformIntProvider { MinInclusive = range.MinInclusive, MaxInclusive = range.MaxInclusive };
    }

    internal static bool IsSlimeChunk(long seed, int x, int z)
    {
        var mixed = unchecked(seed + (int)(x * x * 4987142) + x * 5947611 + (long)(z * z) * 4392871 + z * 389711) ^ 987234911L;
        var state = (mixed ^ 0x5DEECE66DL) & ((1L << 48) - 1);
        int bits;
        int value;
        do
        {
            state = (state * 0x5DEECE66DL + 11) & ((1L << 48) - 1);
            bits = (int)(state >> 17);
            value = bits % 10;
        } while (unchecked(bits - value + 9) < 0);
        return value == 0;
    }
}
