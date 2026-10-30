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
public class VanillaFeaturePlacement
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

    private static readonly FeatureRegistry registry = new(WorldgenData.Vanilla);

    public sealed record FeatureCase(JsonElement Entry)
    {
        public override string ToString() =>
            $"{this.Entry.GetProperty("feature").GetString()} "
            + $"({this.Entry.GetProperty("chunkX")}, {this.Entry.GetProperty("chunkZ")})";
    }

    public static TheoryData<FeatureCase> FeatureCases()
    {
        var cases = new TheoryData<FeatureCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("features"))
        {
            if (!pending.Contains(entry.GetProperty("feature").GetString()!))
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
        var region = Region(chunkX, chunkZ, entry.GetProperty("seed").GetInt64(), layers);
        var biome = entry.GetProperty("biome").GetString()!;
        var context = new DecorationContext(registry, region, _ => biome, MinY, Height);
        var feature = registry.Placed(entry.GetProperty("feature").GetString()!);

        var random = DecorationRandom(entry, chunkX, chunkZ);
        var origin = new Vector(chunkX * 16, MinY, chunkZ * 16);
        var placed = feature.PlaceWithBiomeCheck(context, random, origin);

        var expected = entry.GetProperty("changes").EnumerateArray().Select(ExpectedChange).ToList();
        var actual = Changes(region, chunkX, chunkZ, layers);
        Assert.Equal(expected, actual);
        Assert.Equal(entry.GetProperty("placed").GetBoolean(), placed);
    }

    // Vanilla's decoration random: a population seed from the chunk's corner, then the feature's step and index.
    private static WorldgenRandom DecorationRandom(JsonElement entry, int chunkX, int chunkZ)
    {
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
    private static WorldGenRegion Region(int centerX, int centerZ, long seed, (IBlock Block, int Top)[] layers)
    {
        var chunks = new Dictionary<(int X, int Z), IChunk>();

        for (var chunkX = centerX - RegionRadius; chunkX <= centerX + RegionRadius; chunkX++)
        {
            for (var chunkZ = centerZ - RegionRadius; chunkZ <= centerZ + RegionRadius; chunkZ++)
            {
                chunks[(chunkX, chunkZ)] = LayeredChunk(chunkX, chunkZ, layers);
            }
        }

        // Neither the biome source nor the region's random is used: features see the case's biome, and these
        // features don't draw from the region's random.
        var biomes = new FixedBiomeSource(CodecRegistry.Biomes.Plains);
        var random = new XoroshiroRandomSource(0L);

        return new WorldGenRegion(chunks, centerX, centerZ, seed, MinY, Height, SeaLevel, biomes, random, false);
    }

    private static Chunk LayeredChunk(int chunkX, int chunkZ, (IBlock Block, int Top)[] layers)
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

        WorldgenHeightmaps.Update(chunk, MinY, Height);
        return chunk;
    }

    private static string ExpectedChange(JsonElement change)
    {
        var block = VanillaTerrainGeneration.ParseBlock(change[3].GetString()!);

        return Change(change[0].GetInt32(), change[1].GetInt32(), change[2].GetInt32(), block);
    }

    // The writable chunks' blocks that differ from the layers, in the capture's z, x, y order.
    private static List<string> Changes(WorldGenRegion region, int chunkX, int chunkZ, (IBlock, int)[] layers)
    {
        var changes = new List<string>();

        for (var z = chunkZ * 16 - 16; z < chunkZ * 16 + 32; z++)
        {
            for (var x = chunkX * 16 - 16; x < chunkX * 16 + 32; x++)
            {
                for (var y = MinY; y < MinY + Height; y++)
                {
                    var block = region.GetBlock(new Vector(x, y, z));

                    if (!block.IsSameState(LayerBlock(layers, y)))
                    {
                        changes.Add(Change(x, y, z, block));
                    }
                }
            }
        }

        return changes;
    }

    private static string Change(int x, int y, int z, IBlock block)
    {
        var properties = block.GetProperties()
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => $"{property.Key}={property.Value}");

        return $"({x}, {y}, {z}) {block.UnlocalizedName}[{string.Join(", ", properties)}]";
    }
}
