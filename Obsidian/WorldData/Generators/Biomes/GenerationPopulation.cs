using Obsidian.API.Entities;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.WorldData.Fluids;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>Initial creature packs, kept in the protochunk until the level publishes its entities.</summary>
internal static class GenerationPopulation
{
    private sealed record Size(float Width, float Height, bool Summonable);

    private static readonly IReadOnlyDictionary<string, Size> sizes = ReadSizes();

    public static WorldgenRandom Random(long seed, int chunkX, int chunkZ)
    {
        var random = new WorldgenRandom(new LegacyRandomSource(0));
        random.SetDecorationSeed(seed, chunkX << 4, chunkZ << 4);
        return random;
    }

    public static void Spawn(IWorldGenLevel region, SpawnSettings settings, int chunkX, int chunkZ,
        bool hasCeiling, IRandomSource random, ILevel? host = null)
    {
        var choices = settings.Categories.GetValueOrDefault("creature") ?? [];
        var weight = choices.Sum(choice => choice.Weight);
        if (weight <= 0)
            return;

        var minX = chunkX << 4;
        var minZ = chunkZ << 4;
        while (random.NextFloat() < settings.CreatureProbability)
        {
            var selected = random.NextInt(weight);
            var choice = choices[0];
            foreach (var candidate in choices)
            {
                selected -= candidate.Weight;
                if (selected < 0)
                {
                    choice = candidate;
                    break;
                }
            }

            var count = choice.Count?.Sample(random)
                ?? random.NextInt(choice.MaxCount - choice.MinCount + 1) + choice.MinCount;
            var x = minX + random.NextInt(16);
            var z = minZ + random.NextInt(16);
            var originX = x;
            var originZ = z;
            var spawned = 0;

            // NaturalSpawner uses four attempts per member. Even a successful attempt jitters the next position;
            // collision/rule failures skip that jitter, while invalid placement still consumes it.
            for (var member = 0; member < count; member++)
            {
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    var position = Top(region, choice.Type, x, z, hasCeiling);
                    var success = false;
                    if (TrySize(choice.Type, out var size, out var definition) && size.Summonable
                        && Fits(region, choice.Type, position, definition))
                    {
                        var point = new VectorD(
                            Math.Clamp((double)x, minX + size.Width, minX + 16d - size.Width),
                            position.Y,
                            Math.Clamp((double)z, minZ + size.Width, minZ + 16d - size.Width));
                        if (!IsFree(region, point, size))
                            continue;

                        if (!CanSpawn(region, choice.Type, position, definition)
                            || definition is not null && (host is null
                                || !PluginSpawnRules.CanSpawn(definition, host, (Vector)point.Floor())))
                        {
                            continue;
                        }

                        var yaw = random.NextFloat() * 360f;
                        var data = new NbtCompound();
                        // Age/appearance use the region/entity random, independently of the pack's random.
                        if (spawned > 0 && choice.Type is not ("minecraft:frog" or "minecraft:parrot")
                            && region.Random.NextFloat() <= 0.05f)
                            data.Add(new NbtTag<int>("Age", -24000));
                        region.AddEntity(new GeneratedEntity(choice.Type, point, yaw) { Data = data });
                        spawned++;
                        success = true;
                    }

                    x += random.NextInt(5) - random.NextInt(5);
                    z += random.NextInt(5) - random.NextInt(5);
                    while (x < minX || x >= minX + 16 || z < minZ || z >= minZ + 16)
                    {
                        x = originX + random.NextInt(5) - random.NextInt(5);
                        z = originZ + random.NextInt(5) - random.NextInt(5);
                    }

                    if (success)
                        break;
                }
            }
        }
    }

    private static Vector Top(IWorldGenLevel region, string type, int x, int z, bool ceiling)
    {
        var map = type is "minecraft:ocelot" or "minecraft:parrot"
            ? HeightmapType.MotionBlocking : HeightmapType.MotionBlockingNoLeaves;
        var y = region.GetHeight(map, x, z);
        if (ceiling)
        {
            while (y > region.MinY && !region.GetBlock(new Vector(x, --y, z)).IsAir)
            {
            }
            while (y > region.MinY && region.GetBlock(new Vector(x, --y, z)).IsAir)
            {
            }
        }

        var below = region.GetBlock(new Vector(x, y - 1, z));
        if (below.IsAir || TagsRegistry.Block.Leaves.Entries.Contains(below.RegistryId))
            y--;
        return new Vector(x, y, z);
    }

    private static bool Fits(IWorldGenLevel region, string id, Vector position, EntityDefinition? definition)
    {
        var feet = region.GetBlock(position);
        var floor = region.GetBlock(position + Vector.Down);
        var above = region.GetBlock(position + Vector.Up);
        if (definition?.Settings.SpawnPlacement is SpawnPlacementType placement)
            return PluginSpawnRules.Fits(placement, feet, floor, above);

        if (definition is not null)
            return PluginSpawnRules.Fits(SpawnPlacementType.OnGround, feet, floor, above);
        if (!EntityNbt.TryParseType(id, out var type))
            return false;
        if (type == EntityType.Strider)
            return FluidState.Of(feet).Kind == FluidKind.Lava;
        if (!FluidState.Of(feet).IsEmpty || !FluidState.Of(above).IsEmpty
            || !floor.HasFullTopCollisionFace()
            || BlockCollisionShapes.Get(feet).Count > 0 || BlockCollisionShapes.Get(above).Count > 0)
        {
            return false;
        }

        return true;
    }

    private static bool CanSpawn(IWorldGenLevel region, string id, Vector position, EntityDefinition? definition)
    {
        if (definition is not null)
            return true;
        if (!EntityNbt.TryParseType(id, out var type))
            return false;
        if (type == EntityType.Strider)
        {
            var above = position + Vector.Up;
            while (FluidState.Of(region.GetBlock(above)).Kind == FluidKind.Lava)
                above += Vector.Up;
            return region.GetBlock(above).IsAir;
        }

        var floor = region.GetBlock(position + Vector.Down);
        var biome = region.GetBiome(position);
        var tag = type switch
        {
            EntityType.Armadillo => "armadillo_spawnable_on",
            EntityType.Camel => "camels_spawnable_on",
            EntityType.Mooshroom => "mooshrooms_spawnable_on",
            EntityType.Turtle => "sand",
            EntityType.Wolf => "wolves_spawnable_on",
            EntityType.Goat => "goats_spawnable_on",
            EntityType.Rabbit => "rabbits_spawnable_on",
            EntityType.Frog => "frogs_spawnable_on",
            EntityType.Parrot => "parrots_spawnable_on",
            EntityType.PolarBear when TagsRegistry.Worldgen.Biome.PolarBearsSpawnOnAlternateBlocks.Entries.Contains(biome.Id)
                => "polar_bears_spawnable_on_alternate",
            _ => "animals_spawnable_on"
        };
        if (!TagsRegistry.TryGet("block", $"minecraft:{tag}", out var blocks)
            || !blocks.Entries.Contains(floor.RegistryId) || region.UnlitBrightness <= 8)
            return false;
        if (type == EntityType.Turtle)
            return position.Y < region.SeaLevel + 4;
        if (type == EntityType.Ocelot)
            return position.Y >= region.SeaLevel && region.Random.NextInt(3) != 0;
        return true;
    }

    private static bool IsFree(IWorldGenLevel region, VectorD point, Size size)
    {
        var half = size.Width / 2d;
        var bounds = new BoundingBox(point - new VectorD(half, 0, half),
            point + new VectorD(half, size.Height, half));
        for (var x = (int)Math.Floor(bounds.Min.X); x <= (int)Math.Floor(bounds.Max.X); x++)
        {
            for (var y = (int)Math.Floor(bounds.Min.Y) - 1; y <= (int)Math.Floor(bounds.Max.Y); y++)
            {
                for (var z = (int)Math.Floor(bounds.Min.Z); z <= (int)Math.Floor(bounds.Max.Z); z++)
                {
                    foreach (var shape in BlockCollisionShapes.Get(region.GetBlock(new Vector(x, y, z))))
                    {
                        if (MobTerrain.Overlaps(bounds, shape.OffsetBy(new VectorD(x, y, z))))
                            return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool TrySize(string id, out Size size, out EntityDefinition? definition)
    {
        definition = null;
        if (sizes.TryGetValue(id, out size!))
            return true;
        if (EntityDefinitionsRegistry.TryGet(id, out definition))
        {
            size = new Size(definition.Settings.Width, definition.Settings.Height, true);
            return true;
        }

        return false;
    }

    private static Dictionary<string, Size> ReadSizes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.entities.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject()
            .Where(entry => entry.Value.TryGetProperty("width", out _))
            .ToDictionary(entry => entry.Name, entry => new Size(
                entry.Value.GetProperty("width").GetSingle(), entry.Value.GetProperty("height").GetSingle(),
                entry.Value.GetProperty("summonable").GetBoolean()));
    }
}
