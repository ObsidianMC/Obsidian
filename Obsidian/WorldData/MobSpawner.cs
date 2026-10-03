using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Nbt;
using Obsidian.WorldData.Generators;
using System.Reflection;
using System.Text.Json;
using System.IO;

namespace Obsidian.WorldData;

internal sealed partial class MobSpawner(AbstractLevel level)
{
    private static readonly Dictionary<string, Dictionary<string, SpawnerMob[]>> biomeSpawns = LoadBiomeSpawns();
    private static readonly SpawnerMob[] fortressSpawns = LoadFortressSpawns();
    private static SpawnerMob[] LoadFortressSpawns()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.worldgen.structures.nether.json")
            ?? throw new InvalidDataException("Missing Nether structure spawn data.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("minecraft:fortress").GetProperty("spawn_overrides").GetProperty("monster")
            .GetProperty("spawns").Deserialize<SpawnerMob[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    private readonly Random random = new();
    private long ticks;

    private static Dictionary<string, Dictionary<string, SpawnerMob[]>> LoadBiomeSpawns()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.Codecs.biomes.json")
            ?? throw new InvalidDataException("Missing biome spawn data.");
        using var document = JsonDocument.Parse(stream);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return document.RootElement.GetProperty("value").EnumerateArray().ToDictionary(
            entry => entry.GetProperty("name").GetString()!,
            entry => entry.GetProperty("element").GetProperty("spawners").Deserialize<Dictionary<string, SpawnerMob[]>>(options)!);
    }

    internal void PopulateChunk(IChunk chunk)
    {
        while (random.NextSingle() < 0.1f)
            SpawnGroup(chunk, "creature", [], 32, true);
    }

    internal void Tick()
    {
        ticks++;
        var players = level.Players.Values.Where(player => player.Gamemode != Gamemode.Spectator).ToArray();
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
                    (new VectorF(x * 16 + 8, player.Position.Y, z * 16 + 8) - player.Position).MagnitudeSquared() <= 16384)
                    chunks.Add(NumericsHelper.IntsToLong(x, z));
        }
        var mobs = level.Regions.Values.SelectMany(region => region.Entities.Values).OfType<Mob>()
            .Where(mob => mob.Alive && level.IsMobTicking(mob.Position)).ToArray();
        var monsters = mobs.Count(mob => mob is not Animal && mob is not Squid && !mob.PersistenceRequired);
        var creatures = mobs.Count(mob => mob is Animal && !mob.PersistenceRequired);
        var monsterCap = 70 * chunks.Count / 289;
        var waterCreatures = mobs.Count(mob => mob is Squid && !mob.PersistenceRequired);
        var waterCap = 5 * chunks.Count / 289;
        var creatureCap = 10 * chunks.Count / 289;
        foreach (var packed in chunks.OrderBy(_ => random.Next()))
        {
            NumericsHelper.LongToInts(packed, out var cx, out var cz);
            var chunk = level.GetLoadedChunk(cx, cz)!;
            foreach (var spawner in chunk.GetBlockEntities().OfType<DataBlockEntity>().Where(entity => entity.Id == "minecraft:mob_spawner").ToArray())
                TickSpawner(spawner, players);
            if (waterCreatures < waterCap)
                waterCreatures += SpawnGroup(chunk, "water_creature", players, waterCap - waterCreatures);
            if (level.LevelData.Difficulty != Difficulty.Peaceful && monsters < monsterCap)
                monsters += SpawnGroup(chunk, "monster", players, monsterCap - monsters);
            if (ticks % 400 == 0 && creatures < creatureCap)
                creatures += SpawnGroup(chunk, "creature", players, creatureCap - creatures);
        }
    }

    private int SpawnGroup(IChunk chunk, string category, IPlayer[] players, int remaining, bool generation = false)
    {
        var terrain = new MobTerrain(level);
        var x = chunk.X * 16 + random.Next(16);
        var z = chunk.Z * 16 + random.Next(16);
        var top = GetSpawnHeight(chunk, x, z);
        var maxY = level.MinY + level.Height;
        var seaLevel = level.Generator is MojangGenerator generator ? generator.Builder.RandomState.Settings.SeaLevel : 63;
        var y = category == "creature" ? top : category == "water_creature" ? random.Next(seaLevel - 23, seaLevel) :
            random.Next(level.MinY, Math.Clamp(top + 1, level.MinY + 1, maxY));
        var biome = chunk.GetBiome(x, Math.Clamp(y, level.MinY, maxY - 1), z);
        if (!biomeSpawns.TryGetValue(biome.Name, out var categories) || !categories.TryGetValue(category, out var choices) || choices.Length == 0)
            return 0;
        if (category == "monster" && level.Generator is MojangGenerator structureGenerator &&
            structureGenerator.IsInsideFortress(new Vector(x, y, z)))
            choices = fortressSpawns;
        if (category == "monster")
        {
            var updated = choices.ToList();
            if (biome.Name is "minecraft:swamp" or "minecraft:mangrove_swamp" && !updated.Any(entry => entry.Type == "minecraft:bogged"))
                updated.Add(new SpawnerMob { Type = "minecraft:bogged", Weight = 30, MinCount = 4, MaxCount = 4 });
            if (biome.Name == "minecraft:desert" && !updated.Any(entry => entry.Type == "minecraft:parched"))
            {
                updated.RemoveAll(entry => entry.Type == "minecraft:skeleton");
                updated.Add(new SpawnerMob { Type = "minecraft:skeleton", Weight = 50, MinCount = 4, MaxCount = 4 });
                updated.Add(new SpawnerMob { Type = "minecraft:parched", Weight = 50, MinCount = 4, MaxCount = 4 });
            }
            choices = updated.ToArray();
        }
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
        if (choice == null || !EntityNbt.TryParseType(choice.Type, out var type))
            return 0;
        var spawned = 0;
        int? wolfVariant = null;
        var group = type == EntityType.Ghast ? 1 : random.Next(choice.MinCount, choice.MaxCount + 1);
        for (var attempt = 0; attempt < group * 4 && spawned < group && spawned < remaining; attempt++)
        {
            x += random.Next(6) - random.Next(6);
            z += random.Next(6) - random.Next(6);
            if (level.GetLoadedChunk(x >> 4, z >> 4) is not { } candidateChunk)
                continue;
            if (generation)
                y = GetSpawnHeight(candidateChunk, x, z);
            var position = new VectorF(x + 0.5f, y, z + 0.5f);
            if (!generation && (players.Any(player => (player.Position - position).MagnitudeSquared() < 576) ||
                !players.Any(player => (player.Position - position).MagnitudeSquared() <= 16384) ||
                (level.LevelData.SpawnPosition - position).MagnitudeSquared() < 576))
                continue;
            if (type == EntityType.Blaze && (level.Generator is not MojangGenerator fortressGenerator ||
                !fortressGenerator.IsInsideFortress(new Vector(x, y, z))))
                continue;
            var mob = EntitySpawner.CreateMob(level, type);
            if (mob == null)
                continue;
            var feet = terrain.GetBlock(new Vector(x, y, z));
            var floor = terrain.GetBlock(new Vector(x, y - 1, z));
            if (feet == null || floor == null || !terrain.IsFree(mob.Dimension.CreateBBFromPosition(position)) ||
                (type == EntityType.Squid ? feet.Material != Material.Water || y <= seaLevel - 24 || y >= seaLevel - 1 :
                    feet.IsLiquid || type != EntityType.Ghast && (floor.IsLiquid || BlockCollisionShapes.Get(floor).Count == 0)))
                continue;
            var sky = candidateChunk.GetLightLevel(x, y, z, LightType.Sky);
            var block = candidateChunk.GetLightLevel(x, y, z, LightType.Block);
            if (category == "creature")
            {
                if ((type == EntityType.Mooshroom ? floor.Material != Material.Mycelium : type == EntityType.Wolf ?
                        !TagsRegistry.Block.WolvesSpawnableOn.Entries.Contains(floor.RegistryId) : floor.Material != Material.GrassBlock) ||
                    Math.Max(block, Math.Max(0, sky - (!generation && level.DayTime is >= 12000 and < 23000 ? 11 : 0))) <= 8)
                    continue;
            }
            else if (category == "water_creature")
            {
                if (terrain.GetBlock(new Vector(x, y + 1, z))?.Material != Material.Water)
                    continue;
            }
            else if (level.DimensionName == "minecraft:the_nether")
            {
                if (type == EntityType.Ghast && random.Next(20) != 0 ||
                    type is not EntityType.Ghast and not EntityType.MagmaCube and not EntityType.Blaze && block > 11 ||
                    type == EntityType.Blaze && block > 11)
                    continue;
            }
            else if (type == EntityType.Slime)
            {
                var localBiome = candidateChunk.GetBiome(x, y, z);
                ReadOnlySpan<float> phases = [1, 0.75f, 0.5f, 0.25f, 0, 0.25f, 0.5f, 0.75f];
                var moon = phases[(int)(level.LevelData.Time / 24000 % 8 + 8) % 8];
                var swamp = localBiome.Name == "minecraft:swamp" && y is > 50 and < 70 && random.NextSingle() < moon &&
                    Math.Max(block, Math.Max(0, sky - (level.DayTime is >= 12000 and < 23000 ? 11 : 0))) <= random.Next(8);
                if (!swamp && !(y < 40 && IsSlimeChunk(level.LevelData.RandomSeed, x >> 4, z >> 4) && random.Next(10) == 0))
                    continue;
            }
            else if (block != 0 || sky > random.Next(32) ||
                Math.Max(block, Math.Max(0, sky - (level.DayTime is >= 12000 and < 23000 ? 11 : 0))) > random.Next(8))
                continue;
            if (type is EntityType.Husk or EntityType.Parched or EntityType.Stray && sky != 15)
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
            if (mob is Animal animal && spawned > 0 && random.NextSingle() < 0.05f)
                animal.IsBaby = true;
            if (!terrain.IsFree(mob.Dimension.CreateBBFromPosition(position)) || level.GetEntitiesInRange(position, 2)
                .Any(entity => MobTerrain.Overlaps(mob.Dimension.CreateBBFromPosition(position), entity.Dimension.CreateBBFromPosition(entity.Position))))
                continue;
            level.SpawnEntity(mob);
            spawned++;
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
