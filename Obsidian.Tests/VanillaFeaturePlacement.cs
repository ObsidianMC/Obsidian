using Obsidian.API;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Replays 26.3 single placed-feature captures (<c>features</c>): one placed feature on layered terrain, with a fixed
/// biome instead of biome selection, placed with the decoration random of the case's chunk, step and index. The
/// blocks of the writable 3x3 chunks that differ from the layers must match vanilla's.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public class VanillaFeaturePlacement
{
    private const int MinY = -64;
    private const int Height = 384;
    private const int SeaLevel = 63;

    // The chunks around the decorated one that the replay builds. Vanilla's region reaches further (the capture's
    // regionRadius), but these features don't read beyond two chunks.
    private const int RegionRadius = 2;

    // Captured placed features whose feature types aren't implemented yet.
    private static readonly HashSet<string> pending = [];

    private static readonly FeatureRegistry registry = new(WorldgenData.Vanilla)
    {
        Template = VanillaFeatureTemplates.Get
    };

    [Fact]
    public void FeaturesMatchVanilla()
    {
        var replay = new Replay();
        var cases = VanillaWorldgenCaptures.CurrentCases("features").Where(entry => !IsPending(entry));

        FixtureCases.AssertAll(cases, Name, replay.Assert);
    }

    private static bool IsPending(JsonElement entry) =>
        !entry.TryGetProperty("probe", out _) && pending.Contains(entry.GetProperty("feature").ToString());

    private static string Name(JsonElement entry)
    {
        var label = entry.TryGetProperty("name", out var name)
            ? name.GetString()!
            : entry.GetProperty("feature").ToString();

        if (label.Length > 80)
            label = label[..80] + "...";

        return $"{label} ({entry.GetProperty("chunkX")}, {entry.GetProperty("chunkZ")})";
    }

    /// <summary>
    /// Replays the cases one at a time, sharing what they would each build alike: a chunk of each layering, which a
    /// case's chunks are copies of, and the pipelines that post-process.
    /// </summary>
    private sealed class Replay
    {
        private readonly Dictionary<(string Layers, int MinY, int Height), Chunk> layered = [];
        private readonly Dictionary<(WorldgenDimension Dimension, long Seed), BiomeTerrain> pipelines = [];

        public void Assert(JsonElement entry)
        {
            if (entry.TryGetProperty("probe", out _))
                AssertComponent(entry, this);
            else
                AssertPlacement(entry, this);
        }

        // A chunk of the case's layers, without its edits.
        public Chunk Layered(int chunkX, int chunkZ, JsonElement entry, (IBlock Block, int Top)[] layers)
        {
            var key = (entry.GetProperty("layers").GetRawText(), CaseMinY(entry), CaseHeight(entry));

            if (!this.layered.TryGetValue(key, out var template))
            {
                template = LayeredChunk(0, 0, layers, key.Item2, key.Item3);
                this.layered.Add(key, template);
            }

            return (Chunk)template.Clone(chunkX, chunkZ);
        }

        public BiomeTerrain Pipeline(JsonElement entry)
        {
            var key = (Dimension(entry), entry.GetProperty("seed").GetInt64());

            if (!this.pipelines.TryGetValue(key, out var pipeline))
            {
                pipeline = BiomeTerrain.Create(key.Item1, key.Item2);
                this.pipelines.Add(key, pipeline);
            }

            return pipeline;
        }
    }

    private static void AssertPlacement(JsonElement entry, Replay replay)
    {
        var layers = Layers(entry);
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();
        var region = Region(chunkX, chunkZ, entry, layers, replay, out var chunks);
        var biome = entry.GetProperty("biome").GetString()!;
        var context = Context(entry, region, biome);
        var feature = registry.Placed(entry.GetProperty("feature"));

        var random = DecorationRandom(entry, chunkX, chunkZ);
        var origin = Origin(entry, chunkX, chunkZ);
        var placed = feature.PlaceWithBiomeCheck(context, random, origin);
        var nextRandom = random.NextInt();

        var expected = entry.GetProperty("changes").EnumerateArray().Select(ExpectedChange).ToList();
        var actual = Changes(region, chunkX, chunkZ, layers, entry);

        AssertExpectedChanges(entry, expected, actual);
        Assert.Equal(expected, actual);
        Assert.Equal(entry.GetProperty("placed").GetBoolean(), placed);
        Assert.Equal(entry.GetProperty("nextRandom").GetInt32(), nextRandom);

        if (entry.TryGetProperty("expectBlocks", out var expectBlocks))
            AssertExpectedBlocks(expectBlocks, expected, actual);

        if (entry.TryGetProperty("nextRegionRandom", out var nextRegionRandom))
            Assert.Equal(nextRegionRandom.GetInt32(), region.Random.NextInt());

        if (entry.TryGetProperty("fluidTicks", out var expectedTicks))
            AssertFluidTicks(entry, expectedTicks, chunks);

        if (entry.TryGetProperty("recordBlockEntities", out _))
            AssertBlockEntities(entry, chunks);

        if (entry.TryGetProperty("marks", out var expectedMarks))
            AssertMarks(entry, expectedMarks, chunks, region.MinY);

        if (entry.TryGetProperty("postProcess", out _))
            AssertPostProcessing(entry, replay.Pipeline(entry), region, chunks, layers);
    }

    // A case can require that both vanilla and the replay change blocks, or that neither does.
    private static void AssertExpectedChanges(JsonElement entry, List<string> expected, List<string> actual)
    {
        if (!entry.TryGetProperty("expectChanges", out var expectChanges))
            return;

        if (expectChanges.GetBoolean())
        {
            Assert.NotEmpty(expected);
            Assert.NotEmpty(actual);
        }
        else
        {
            Assert.Empty(expected);
            Assert.Empty(actual);
        }
    }

    private static void AssertExpectedBlocks(JsonElement expectBlocks, List<string> expected, List<string> actual)
    {
        foreach (var block in expectBlocks.EnumerateArray())
        {
            var statePrefix = $" {block.GetString()}[";

            Assert.Contains(expected, change => change.Contains(statePrefix, StringComparison.Ordinal));
            Assert.Contains(actual, change => change.Contains(statePrefix, StringComparison.Ordinal));
        }
    }

    private static void AssertFluidTicks(
        JsonElement entry, JsonElement expectedTicks, Dictionary<(int X, int Z), IChunk> chunks)
    {
        var expected = expectedTicks.EnumerateArray()
            .Select(tick => FluidTick(Position(tick), tick[3].GetString()!, tick[4].GetInt32(), tick[5].GetInt32()))
            .ToList();
        var actual = FeatureFluidTicks(chunks, ChunkX(entry), ChunkZ(entry));

        if (entry.TryGetProperty("expectFluidTicks", out var expectTicks) && expectTicks.GetBoolean())
        {
            Assert.NotEmpty(expected);
            Assert.NotEmpty(actual);
        }

        Assert.Equal(expected, actual);
    }

    private static void AssertBlockEntities(JsonElement entry, Dictionary<(int X, int Z), IChunk> chunks)
    {
        var expected = entry.GetProperty("blockEntities").EnumerateArray().Select(ExpectedBlockEntity).ToList();
        var actual = BlockEntities(chunks, ChunkX(entry), ChunkZ(entry));

        Assert.Equal(expected, actual);
    }

    private static void AssertMarks(
        JsonElement entry, JsonElement expectedMarks, Dictionary<(int X, int Z), IChunk> chunks, int minY)
    {
        var expected = expectedMarks.EnumerateArray().Select(Position).ToList();
        var actual = FeatureMarks(chunks, ChunkX(entry), ChunkZ(entry), minY);

        if (entry.TryGetProperty("expectMarks", out var expectMarks) && expectMarks.GetBoolean())
        {
            Assert.NotEmpty(expected);
            Assert.NotEmpty(actual);
        }

        Assert.Equal(expected, actual);
    }

    // The writable chunks' blocks once the pipeline post-processes them (fluid ticks and shape updates).
    private static void AssertPostProcessing(
        JsonElement entry,
        BiomeTerrain pipeline,
        WorldGenRegion region,
        Dictionary<(int X, int Z), IChunk> chunks,
        (IBlock Block, int Top)[] layers)
    {
        var chunkX = ChunkX(entry);
        var chunkZ = ChunkZ(entry);

        for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
        {
            for (var x = chunkX - 1; x <= chunkX + 1; x++)
                pipeline.PostProcess(chunks, x, z);
        }

        var expected = entry.GetProperty("postChanges").EnumerateArray().Select(ExpectedChange).ToList();
        var actual = Changes(region, chunkX, chunkZ, layers, entry);

        Assert.Equal(expected, actual);
    }

    // A probe of one feature component: a block state provider, placement modifier, block predicate or rule test.
    private static void AssertComponent(JsonElement entry, Replay replay)
    {
        var chunkX = ChunkX(entry);
        var chunkZ = ChunkZ(entry);
        var layers = Layers(entry);
        var region = Region(chunkX, chunkZ, entry, layers, replay, out _);
        var biome = entry.GetProperty("biome").GetString()!;
        var context = Context(entry, region, biome);
        var origin = Origin(entry, chunkX, chunkZ);
        var random = DecorationRandom(entry, chunkX, chunkZ);

        switch (entry.GetProperty("probe").GetString())
        {
            case "provider":
                AssertProvider(entry, region, random, origin);
                break;
            case "modifier":
                AssertModifier(entry, context, random, origin);
                break;
            case "predicate":
                AssertPredicate(entry, region, origin);
                break;
            case "rule":
                AssertRule(entry, random, origin);
                break;
            default:
                throw new InvalidOperationException("Unknown feature component probe.");
        }

        Assert.Equal(entry.GetProperty("nextRandom").GetInt32(), random.NextInt());
    }

    private static void AssertProvider(JsonElement entry, WorldGenRegion region, WorldgenRandom random, Vector origin)
    {
        var provider = registry.Provider(entry.GetProperty("provider"));
        var required = entry.TryGetProperty("requiredState", out var requiredState) && requiredState.GetBoolean();
        var state = required
            ? FeatureRegistry.State(provider, region, random, origin)
            : provider(region, random, origin);
        var expected = entry.GetProperty("state");

        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(state);
        }
        else
        {
            Assert.NotNull(state);
            Assert.True(VanillaTerrainGeneration.ParseBlock(expected.GetString()!).IsSameState(state));
        }

        if (!entry.TryGetProperty("compatibilityProvider", out var compatibilityProvider))
            return;

        var compatibilityRandom = DecorationRandom(entry, ChunkX(entry), ChunkZ(entry));
        var compatibility = registry.Provider(compatibilityProvider);

        var compatibilityState = compatibility(region, compatibilityRandom, origin);

        Assert.NotNull(compatibilityState);
        Assert.True(compatibilityState.IsSameState(state));
        Assert.Equal(entry.GetProperty("nextRandom").GetInt32(), compatibilityRandom.NextInt());
    }

    private static void AssertModifier(
        JsonElement entry, DecorationContext context, WorldgenRandom random, Vector origin)
    {
        var positions = new List<Vector>();
        var modifier = registry.Modifier(entry.GetProperty("modifier"));

        modifier(new PlacementContext(context, null), random, origin, positions);

        var expected = entry.GetProperty("positions").EnumerateArray().Select(Position).ToList();
        Assert.Equal(expected, positions);
    }

    private static void AssertPredicate(JsonElement entry, WorldGenRegion region, Vector origin)
    {
        var predicate = registry.Predicate(entry.GetProperty("predicate"));

        var result = predicate(region, origin);

        Assert.Equal(entry.GetProperty("result").GetBoolean(), result);
    }

    private static void AssertRule(JsonElement entry, WorldgenRandom random, Vector origin)
    {
        var rule = registry.Rule(entry.GetProperty("rule"));
        var state = FeatureRegistry.ReadBlock(entry.GetProperty("inputState"));

        var result = rule(state, origin, random);

        Assert.Equal(entry.GetProperty("result").GetBoolean(), result);
    }

    private static int ChunkX(JsonElement entry) => entry.GetProperty("chunkX").GetInt32();

    private static int ChunkZ(JsonElement entry) => entry.GetProperty("chunkZ").GetInt32();

    private static int CaseMinY(JsonElement entry) =>
        entry.TryGetProperty("minY", out var minY) ? minY.GetInt32() : MinY;

    private static int CaseHeight(JsonElement entry) =>
        entry.TryGetProperty("height", out var height) ? height.GetInt32() : Height;

    private static DecorationContext Context(JsonElement entry, WorldGenRegion region, string biome)
    {
        var settings = Dimension(entry).Settings;

        return new DecorationContext(registry, region, _ => biome, settings.MinY, settings.Height);
    }

    private static WorldgenDimension Dimension(JsonElement entry)
    {
        var dimension = entry.TryGetProperty("dimension", out var value) ? value.GetString() : "minecraft:overworld";

        return dimension switch
        {
            "minecraft:overworld" => WorldgenDimension.Overworld,
            "minecraft:the_nether" => WorldgenDimension.Nether,
            "minecraft:the_end" => WorldgenDimension.End,
            _ => throw new InvalidOperationException($"Unknown feature capture dimension {dimension}.")
        };
    }

    private static Vector Position(JsonElement coordinates) =>
        new(coordinates[0].GetInt32(), coordinates[1].GetInt32(), coordinates[2].GetInt32());

    private static Vector Origin(JsonElement entry, int chunkX, int chunkZ) =>
        entry.TryGetProperty("origin", out var origin)
            ? Position(origin)
            : new Vector(chunkX * 16, CaseMinY(entry), chunkZ * 16);

    internal static List<Vector> FeatureMarks(
        Dictionary<(int X, int Z), IChunk> chunks, int centerX, int centerZ, int minY)
    {
        var result = new List<Vector>();

        for (var z = centerZ - 1; z <= centerZ + 1; z++)
        {
            for (var x = centerX - 1; x <= centerX + 1; x++)
            {
                var chunk = (Chunk)chunks[(x, z)];
                result.AddRange(chunk.PostProcessing.OrderBy(position => (position.Y - minY) >> 4));
            }
        }

        return result;
    }

    private static List<string> FeatureFluidTicks(Dictionary<(int X, int Z), IChunk> chunks, int centerX, int centerZ)
    {
        var result = new List<string>();

        for (var z = centerZ - 1; z <= centerZ + 1; z++)
        {
            for (var x = centerX - 1; x <= centerX + 1; x++)
            {
                var chunk = (Chunk)chunks[(x, z)];
                foreach (var tick in chunk.FluidTicks.Pack())
                {
                    result.Add(FluidTick(tick.Position, tick.Type.Name(), tick.Delay, tick.Priority));
                }
            }
        }

        return result;
    }

    private static string FluidTick(Vector position, string type, int delay, int priority) =>
        $"{position.X},{position.Y},{position.Z} {type} {delay} {priority}";

    // A captured block entity as "x y z type data", with the data as canonical JSON.
    internal static string ExpectedBlockEntity(JsonElement record)
    {
        var position = Position(record);
        var type = record[3].GetString();
        var data = CanonicalJson(record[4]);

        return $"{position.X} {position.Y} {position.Z} {type} {data}";
    }

    // The block entities of the writable chunks by Y, Z and X, saved without their id and position, like the capture.
    internal static List<string> BlockEntities(Dictionary<(int X, int Z), IChunk> chunks, int centerX, int centerZ)
    {
        var saved = new List<(Vector Position, string Record)>();

        for (var z = centerZ - 1; z <= centerZ + 1; z++)
        {
            for (var x = centerX - 1; x <= centerX + 1; x++)
            {
                var chunk = (Chunk)chunks[(x, z)];

                foreach (var blockEntity in chunk.BlockEntities.Values)
                {
                    var tag = BlockEntityNbt.Save(blockEntity);
                    var type = tag.GetString("id");

                    foreach (var metadata in (string[])["id", "x", "y", "z", "keepPacked"])
                    {
                        tag.Remove(metadata);
                    }

                    if (tag.TryGetTag<NbtCompound>("components", out var components) && components.Count == 0)
                    {
                        tag.Remove("components");
                    }

                    var position = blockEntity.BlockPosition;
                    var data = CanonicalJson(JsonSerializer.SerializeToElement(NbtJson(tag)));
                    var record = $"{position.X} {position.Y} {position.Z} {type} {data}";

                    saved.Add((position, record));
                }
            }
        }

        return saved
            .OrderBy(entity => entity.Position.Y)
            .ThenBy(entity => entity.Position.Z)
            .ThenBy(entity => entity.Position.X)
            .Select(entity => entity.Record)
            .ToList();
    }

    internal static string ExpectedEntity(JsonElement record)
    {
        var position = new VectorD(record[0].GetDouble(), record[1].GetDouble(), record[2].GetDouble());
        var data = CanonicalJson(record[6]);

        return EntityRecord(position, record[3].GetSingle(), record[4].GetSingle(), record[5].GetString()!, data);
    }

    /// <summary>
    /// The entities generation added to the region's chunks, by chunk in z, x order and in each chunk in the order they
    /// were added, with the fields the capture keeps: an end crystal's beam target, invulnerability and bottom.
    /// </summary>
    internal static List<string> Entities(Dictionary<(int X, int Z), IChunk> chunks, int centerX, int centerZ)
    {
        var records = new List<string>();

        for (var z = centerZ - 1; z <= centerZ + 1; z++)
        {
            for (var x = centerX - 1; x <= centerX + 1; x++)
            {
                foreach (var entity in ((Chunk)chunks[(x, z)]).PendingEntities)
                {
                    var data = CanonicalJson(JsonSerializer.SerializeToElement(EntityFields(entity)));

                    records.Add(EntityRecord(entity.Position, entity.Yaw, entity.Pitch, entity.Type, data));
                }
            }
        }

        return records;
    }

    // Generated data leaves out what the entity loads by default: not invulnerable, and a crystal shows its bottom.
    private static JsonObject EntityFields(API.World.Features.GeneratedEntity entity)
    {
        var fields = new JsonObject();

        if (entity.Data.TryGetTag<NbtArray<int>>("beam_target", out var beam))
        {
            fields["beam_target"] = NbtJson(beam);
        }

        var invulnerable = entity.Data.TryGetTag<NbtTag<bool>>("Invulnerable", out var flag) && flag.Value;
        fields["Invulnerable"] = invulnerable ? 1 : 0;

        if (entity.Type == "minecraft:end_crystal")
        {
            var bottom = !entity.Data.TryGetTag<NbtTag<bool>>("ShowBottom", out var shown) || shown.Value;
            fields["ShowBottom"] = bottom ? 1 : 0;
        }

        return fields;
    }

    private static string EntityRecord(VectorD position, float yaw, float pitch, string type, string data)
    {
        var values = new[] { position.X, position.Y, position.Z }
            .Select(value => value.ToString("R", CultureInfo.InvariantCulture));
        var rotation = new[] { yaw, pitch }.Select(value => value.ToString("R", CultureInfo.InvariantCulture));

        return $"{string.Join(' ', values)} {string.Join(' ', rotation)} {type} {data}";
    }

    // Vanilla's NbtOps-to-JsonOps form of a tag: compounds as objects, lists and arrays as arrays, booleans as bytes.
    private static JsonNode NbtJson(INbtTag tag) => tag switch
    {
        NbtCompound compound => NbtObject(compound),
        NbtList list => new JsonArray(list.Select(NbtJson).ToArray()),
        NbtArray<byte> bytes => new JsonArray(bytes.GetArray().Select(value => (JsonNode)value).ToArray()),
        NbtArray<int> ints => new JsonArray(ints.GetArray().Select(value => (JsonNode)value).ToArray()),
        NbtArray<long> longs => new JsonArray(longs.GetArray().Select(value => (JsonNode)value).ToArray()),
        NbtTag<string> text => text.Value,
        NbtTag<bool> flag => flag.Value ? 1 : 0,
        NbtTag<byte> value => value.Value,
        NbtTag<short> value => value.Value,
        NbtTag<int> value => value.Value,
        NbtTag<long> value => value.Value,
        NbtTag<float> value => value.Value,
        NbtTag<double> value => value.Value,
        _ => throw new InvalidOperationException($"Unexpected tag {tag}.")
    };

    private static JsonObject NbtObject(NbtCompound compound)
    {
        var fields = compound.Select(field => KeyValuePair.Create(field.Key, NbtJson(field.Value)));

        return new JsonObject(fields);
    }

    // JSON with sorted keys and numbers by value, so vanilla's and Obsidian's data compare as text.
    private static string CanonicalJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var fields = element.EnumerateObject()
                    .OrderBy(field => field.Name, StringComparer.Ordinal)
                    .Select(field => $"{JsonSerializer.Serialize(field.Name)}:{CanonicalJson(field.Value)}");

                return $"{{{string.Join(',', fields)}}}";
            }
            case JsonValueKind.Array:
            {
                var items = element.EnumerateArray().Select(CanonicalJson);

                return $"[{string.Join(',', items)}]";
            }
            case JsonValueKind.Number:
                return element.GetDouble().ToString("R", CultureInfo.InvariantCulture);
            case JsonValueKind.String:
                return JsonSerializer.Serialize(element.GetString());
            default:
                return element.GetRawText();
        }
    }

    // Vanilla's decoration random: a population seed from the chunk's corner, then the feature's step and index.
    private static WorldgenRandom DecorationRandom(JsonElement entry, int chunkX, int chunkZ)
    {
        if (entry.TryGetProperty("randomSeed", out var randomSeed))
        {
            return new WorldgenRandom(new LegacyRandomSource(randomSeed.GetInt64()));
        }

        var random = new WorldgenRandom(new XoroshiroRandomSource(0L));
        var seed = entry.GetProperty("seed").GetInt64();
        var decorationSeed = random.SetDecorationSeed(seed, chunkX * 16, chunkZ * 16);
        var index = entry.GetProperty("index").GetInt32();
        var step = entry.GetProperty("step").GetInt32();
        random.SetFeatureSeed(decorationSeed, index, step);

        return random;
    }

    // The layers' blocks from the bottom up, and the highest Y of each.
    private static (IBlock Block, int Top)[] Layers(JsonElement entry) => entry.GetProperty("layers")
        .EnumerateArray()
        .Select(layer => (BlocksRegistry.Get(layer[0].GetString()!), layer[1].GetInt32()))
        .ToArray();

    private static IBlock LayerBlock((IBlock Block, int Top)[] layers, int y)
    {
        foreach (var (block, top) in layers)
        {
            if (y <= top)
            {
                return block;
            }
        }

        return BlocksRegistry.Air;
    }

    // Layered chunks at the terrain status, with their world generation heightmaps. The region computes the final
    // heightmaps from the blocks and keeps them up to date with every write.
    private static WorldGenRegion Region(
        int centerX,
        int centerZ,
        JsonElement entry,
        (IBlock Block, int Top)[] layers,
        Replay replay,
        out Dictionary<(int X, int Z), IChunk> chunks)
    {
        var edits = Edits(entry);
        chunks = new Dictionary<(int X, int Z), IChunk>();

        for (var chunkX = centerX - RegionRadius; chunkX <= centerX + RegionRadius; chunkX++)
        {
            for (var chunkZ = centerZ - RegionRadius; chunkZ <= centerZ + RegionRadius; chunkZ++)
            {
                var chunk = replay.Layered(chunkX, chunkZ, entry, layers);
                ApplyEdits(chunk, entry, edits);
                chunks[(chunkX, chunkZ)] = chunk;
            }
        }

        // Features see the case's fixed biome; moss carpet also draws from this independent region random.
        var seed = entry.GetProperty("seed").GetInt64();
        var biomes = new FixedBiomeSource(CodecRegistry.Biomes.Plains);
        var regionSeed = entry.TryGetProperty("regionRandomSeed", out var regionRandomSeed)
            ? regionRandomSeed.GetInt64()
            : 0L;
        var random = new XoroshiroRandomSource(regionSeed);

        var minY = CaseMinY(entry);
        var height = CaseHeight(entry);
        var seaLevel = entry.TryGetProperty("seaLevel", out var seaLevelJson) ? seaLevelJson.GetInt32() : SeaLevel;

        return new WorldGenRegion(chunks, centerX, centerZ, seed, minY, height, seaLevel, biomes, random, false);
    }

    private static Chunk LayeredChunk(int chunkX, int chunkZ, (IBlock Block, int Top)[] layers, int minY, int height)
    {
        var chunk = new Chunk(chunkX, chunkZ, minY, height);
        var top = layers[^1].Top;

        for (var y = minY; y <= top; y++)
        {
            var block = LayerBlock(layers, y);

            if (block.IsAir)
                continue;

            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetBlock(x, y, z, block);
            }
        }

        return chunk;
    }

    // The case's edits inside the chunk, then the chunk's world generation heightmaps.
    private static void ApplyEdits(Chunk chunk, JsonElement entry, Dictionary<Vector, IBlock> edits)
    {
        var recordBlockEntities = entry.TryGetProperty("recordBlockEntities", out _);

        foreach (var (position, block) in edits)
        {
            if (position.X >> 4 != chunk.X || position.Z >> 4 != chunk.Z)
                continue;

            chunk.SetBlock(position.X, position.Y, position.Z, block);

            if (recordBlockEntities && block.HasBlockEntity())
                DataBlockEntity.ApplyBlockChange(chunk, position, block);
        }

        WorldgenHeightmaps.Update(chunk, chunk.MinY, chunk.Height);
    }

    private static string ExpectedChange(JsonElement change)
    {
        var block = VanillaTerrainGeneration.ParseBlock(change[3].GetString()!);

        return Change(change[0].GetInt32(), change[1].GetInt32(), change[2].GetInt32(), block);
    }

    // The writable chunks' blocks that differ from the layers, in the capture's z, x, y order. Blocks compare by state
    // id, as IsSameState does, against each Y's layer state unless the case edited the position.
    private static List<string> Changes(
        WorldGenRegion region, int chunkX, int chunkZ, (IBlock, int)[] layers, JsonElement entry)
    {
        var baseline = Edits(entry);
        var layerStates = Enumerable.Range(region.MinY, region.Height)
            .Select(y => LayerBlock(layers, y).GetHashCode())
            .ToArray();
        var changes = new List<string>();

        for (var z = chunkZ * 16 - 16; z < chunkZ * 16 + 32; z++)
        {
            for (var x = chunkX * 16 - 16; x < chunkX * 16 + 32; x++)
            {
                for (var y = region.MinY; y < region.MinY + region.Height; y++)
                {
                    var position = new Vector(x, y, z);
                    var initial = layerStates[y - region.MinY];

                    if (baseline.Count > 0 && baseline.TryGetValue(position, out var edit))
                        initial = edit.GetHashCode();

                    if (region.GetStateId(position) != initial)
                        changes.Add(Change(x, y, z, region.GetBlock(position)));
                }
            }
        }

        return changes;
    }

    private static Dictionary<Vector, IBlock> Edits(JsonElement entry)
    {
        var result = new Dictionary<Vector, IBlock>();

        if (entry.TryGetProperty("edits", out var edits))
        {
            foreach (var edit in edits.EnumerateArray())
            {
                result[Position(edit)] = FeatureRegistry.ReadBlock(edit[3]);
            }
        }

        return result;
    }

    private static string Change(int x, int y, int z, IBlock block)
    {
        var properties = block.GetProperties()
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => $"{property.Key}={property.Value}");

        return $"({x}, {y}, {z}) {block.UnlocalizedName}[{string.Join(", ", properties)}]";
    }
}
