using Obsidian.API;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Replays 26.3 single placed-feature captures (<c>features</c>): one placed feature on layered terrain, with a fixed
/// biome instead of biome selection, placed with the decoration random of the case's chunk, step and index. The
/// blocks of the writable 3x3 chunks that differ from the layers must match vanilla's.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(WorldgenCollection.Name)]
public class VanillaFeaturePlacement(WorldgenFixture fixture)
{
    private const int MinY = -64;
    private const int Height = 384;
    private const int SeaLevel = 63;

    // The chunks around the decorated one that the replay builds. Vanilla's region reaches further (the capture's
    // regionRadius), but these features don't read beyond two chunks.
    private const int RegionRadius = 2;

    // Captured placed features whose feature types aren't implemented yet: trees and fallen trees, and spikes.
    private static readonly HashSet<string> pending =
    [
        "minecraft:trees_plains",
        "minecraft:trees_birch_and_oak_leaf_litter",
        "minecraft:trees_dappled_forest",
        "minecraft:ice_spike"
    ];

    private static readonly FeatureRegistry registry = new(WorldgenData.Vanilla)
    {
        Template = VanillaFeatureTemplates.Get
    };

    public sealed record FeatureCase(JsonElement Entry)
    {
        public override string ToString()
        {
            var label = this.Entry.TryGetProperty("name", out var name)
                ? name.GetString()
                : this.Entry.GetProperty("feature").ToString();

            return $"{label} ({this.Entry.GetProperty("chunkX")}, {this.Entry.GetProperty("chunkZ")})";
        }
    }

    public static TheoryData<FeatureCase> FeatureCases()
    {
        var cases = new TheoryData<FeatureCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("features"))
        {
            if (!entry.TryGetProperty("probe", out _)
                && !pending.Contains(entry.GetProperty("feature").ToString()))
            {
                cases.Add(new FeatureCase(entry));
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(FeatureCases))]
    public void PlacedFeatureMatchesVanilla(FeatureCase testCase)
    {
        var entry = testCase.Entry;
        var layers = Layers(entry);
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();
        var region = Region(chunkX, chunkZ, entry, layers, out var chunks);
        var biome = entry.GetProperty("biome").GetString()!;
        var context = new DecorationContext(registry, region, _ => biome, MinY, Height);
        var feature = registry.Placed(entry.GetProperty("feature"));

        var random = DecorationRandom(entry, chunkX, chunkZ);
        var origin = Origin(entry, chunkX, chunkZ);
        var placed = feature.PlaceWithBiomeCheck(context, random, origin);
        var nextRandom = random.NextInt();

        var expected = entry.GetProperty("changes").EnumerateArray().Select(ExpectedChange).ToList();
        var actual = Changes(region, chunkX, chunkZ, layers, entry);
        if (entry.TryGetProperty("expectChanges", out var expectChanges))
        {
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

        Assert.Equal(expected, actual);
        Assert.Equal(entry.GetProperty("placed").GetBoolean(), placed);
        Assert.Equal(entry.GetProperty("nextRandom").GetInt32(), nextRandom);

        if (entry.TryGetProperty("nextRegionRandom", out var nextRegionRandom))
        {
            Assert.Equal(nextRegionRandom.GetInt32(), region.Random.NextInt());
        }

        if (entry.TryGetProperty("postProcess", out _))
        {
            var marks = FeatureMarks(chunks, chunkX, chunkZ);
            var expectedMarks = entry.GetProperty("marks").EnumerateArray().Select(Position).ToList();

            Assert.Equal(expectedMarks, marks);

            var builder = fixture.Builder(MojangDimension.Overworld, entry.GetProperty("seed").GetInt64(), false);

            for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
            {
                for (var x = chunkX - 1; x <= chunkX + 1; x++)
                {
                    builder.PostProcess(chunks, x, z);
                }
            }

            var postExpected = entry.GetProperty("postChanges").EnumerateArray().Select(ExpectedChange).ToList();
            var postActual = Changes(region, chunkX, chunkZ, layers, entry);

            Assert.Equal(postExpected, postActual);
        }

    }

    public static TheoryData<FeatureCase> ComponentCases()
    {
        var cases = new TheoryData<FeatureCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("features"))
        {
            if (entry.TryGetProperty("probe", out _))
            {
                cases.Add(new FeatureCase(entry));
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(ComponentCases))]
    public void FeatureComponentMatchesVanilla(FeatureCase testCase)
    {
        var entry = testCase.Entry;
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();
        var layers = Layers(entry);
        var region = Region(chunkX, chunkZ, entry, layers, out _);
        var biome = entry.GetProperty("biome").GetString()!;
        var context = new DecorationContext(registry, region, _ => biome, MinY, Height);
        var origin = Origin(entry, chunkX, chunkZ);
        var random = DecorationRandom(entry, chunkX, chunkZ);

        switch (entry.GetProperty("probe").GetString())
        {
            case "provider":
            {
                var provider = registry.Provider(entry.GetProperty("provider"));
                var required = entry.TryGetProperty("requiredState", out var requiredState)
                    && requiredState.GetBoolean();
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

                if (entry.TryGetProperty("compatibilityProvider", out var compatibilityProvider))
                {
                    var compatibilityRandom = DecorationRandom(entry, chunkX, chunkZ);
                    var compatibility = registry.Provider(compatibilityProvider);
                    var compatibilityState = compatibility(region, compatibilityRandom, origin);

                    Assert.NotNull(compatibilityState);
                    Assert.True(compatibilityState.IsSameState(state));
                    Assert.Equal(entry.GetProperty("nextRandom").GetInt32(), compatibilityRandom.NextInt());
                }

                break;
            }
            case "modifier":
            {
                var positions = new List<Vector>();
                var modifier = registry.Modifier(entry.GetProperty("modifier"));
                modifier(new PlacementContext(context, null), random, origin, positions);
                var expected = entry.GetProperty("positions").EnumerateArray().Select(Position).ToList();

                Assert.Equal(expected, positions);
                break;
            }
            case "predicate":
            {
                var predicate = registry.Predicate(entry.GetProperty("predicate"));
                var result = predicate(region, origin);

                Assert.Equal(entry.GetProperty("result").GetBoolean(), result);
                break;
            }
            case "rule":
            {
                var rule = registry.Rule(entry.GetProperty("rule"));
                var state = FeatureRegistry.ReadBlock(entry.GetProperty("inputState"));
                var result = rule(state, origin, random);

                Assert.Equal(entry.GetProperty("result").GetBoolean(), result);
                break;
            }
            default:
                throw new InvalidOperationException("Unknown feature component probe.");
        }

        Assert.Equal(entry.GetProperty("nextRandom").GetInt32(), random.NextInt());
    }

    private static Vector Position(JsonElement coordinates) =>
        new(coordinates[0].GetInt32(), coordinates[1].GetInt32(), coordinates[2].GetInt32());

    private static Vector Origin(JsonElement entry, int chunkX, int chunkZ) =>
        entry.TryGetProperty("origin", out var origin) ? Position(origin) : new Vector(chunkX * 16, MinY, chunkZ * 16);

    private static List<Vector> FeatureMarks(Dictionary<(int X, int Z), IChunk> chunks, int centerX, int centerZ)
    {
        var result = new List<Vector>();

        for (var z = centerZ - 1; z <= centerZ + 1; z++)
        {
            for (var x = centerX - 1; x <= centerX + 1; x++)
            {
                var chunk = (Chunk)chunks[(x, z)];
                result.AddRange(chunk.PostProcessing.OrderBy(position => (position.Y - MinY) >> 4));
            }
        }

        return result;
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
        out Dictionary<(int X, int Z), IChunk> chunks)
    {
        chunks = new Dictionary<(int X, int Z), IChunk>();

        for (var chunkX = centerX - RegionRadius; chunkX <= centerX + RegionRadius; chunkX++)
        {
            for (var chunkZ = centerZ - RegionRadius; chunkZ <= centerZ + RegionRadius; chunkZ++)
            {
                chunks[(chunkX, chunkZ)] = LayeredChunk(chunkX, chunkZ, layers, entry);
            }
        }

        // Features see the case's fixed biome; moss carpet also draws from this independent region random.
        var seed = entry.GetProperty("seed").GetInt64();
        var biomes = new FixedBiomeSource(CodecRegistry.Biomes.Plains);
        var regionSeed = entry.TryGetProperty("regionRandomSeed", out var regionRandomSeed)
            ? regionRandomSeed.GetInt64()
            : 0L;
        var random = new XoroshiroRandomSource(regionSeed);

        return new WorldGenRegion(chunks, centerX, centerZ, seed, MinY, Height, SeaLevel, biomes, random, false);
    }

    private static Chunk LayeredChunk(
        int chunkX, int chunkZ, (IBlock Block, int Top)[] layers, JsonElement entry)
    {
        var chunk = new Chunk(chunkX, chunkZ, MinY, Height);
        var top = layers[^1].Top;

        for (var y = MinY; y <= top; y++)
        {
            var block = LayerBlock(layers, y);

            if (block.IsAir)
            {
                continue;
            }

            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    chunk.SetBlock(x, y, z, block);
                }
            }
        }

        foreach (var (position, block) in Edits(entry))
        {
            if (position.X >> 4 == chunkX && position.Z >> 4 == chunkZ)
            {
                chunk.SetBlock(position.X, position.Y, position.Z, block);
            }
        }

        WorldgenHeightmaps.Update(chunk, MinY, Height);
        return chunk;
    }

    private static string ExpectedChange(JsonElement change)
    {
        var block = VanillaTerrainGeneration.ParseBlock(change[3].GetString()!);

        return Change(change[0].GetInt32(), change[1].GetInt32(), change[2].GetInt32(), block);
    }

    // The writable chunks' blocks that differ from the layers, in the capture's z, x, y order.
    private static List<string> Changes(
        WorldGenRegion region, int chunkX, int chunkZ, (IBlock, int)[] layers, JsonElement entry)
    {
        var baseline = Edits(entry);
        var changes = new List<string>();

        for (var z = chunkZ * 16 - 16; z < chunkZ * 16 + 32; z++)
        {
            for (var x = chunkX * 16 - 16; x < chunkX * 16 + 32; x++)
            {
                for (var y = MinY; y < MinY + Height; y++)
                {
                    var position = new Vector(x, y, z);
                    var block = region.GetBlock(position);
                    var initial = baseline.GetValueOrDefault(position) ?? LayerBlock(layers, y);

                    if (!block.IsSameState(initial))
                    {
                        changes.Add(Change(x, y, z, block));
                    }
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
