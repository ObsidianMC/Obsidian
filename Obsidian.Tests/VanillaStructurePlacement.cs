using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.Tests;

/// <summary>
/// Replays the 26.3 <c>structures</c> references (<c>docs/vanilla-captures.md</c>) through the 26.3 pipeline: the
/// piece list of a start, and for some starts the blocks their pieces hold once structures alone are placed in every
/// chunk they reach, on terrain bearded by them.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(WorldgenCollection.Name)]
public class VanillaStructurePlacement
{
    // Reading the 26.3 structures and starting them is shared by a dimension's cases.
    private static readonly ConcurrentDictionary<(string Dimension, long Seed), Lazy<BiomeTerrain>> pipelines = new();

    public static TheoryData<string> PieceCases() => Names(_ => true);

    public static TheoryData<string> BlockCases() => Names(entry => entry.TryGetProperty("blocks", out _));

    [Theory]
    [MemberData(nameof(PieceCases))]
    public void PiecesMatchVanilla(string name)
    {
        var entry = Case(name);
        var start = Start(entry, out _);
        var lines = start.Pieces.Select(VanillaStructureGeneration.Describe);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));

        Assert.Equal(entry.GetProperty("pieceCount").GetInt32(), start.Pieces.Count);
        Assert.Equal(entry.GetProperty("pieces").GetString(), hash);
    }

    [Theory]
    [MemberData(nameof(BlockCases))]
    public void BlocksMatchVanilla(string name)
    {
        var entry = Case(name);
        var start = Start(entry, out var pipeline);
        var expected = entry.GetProperty("blocks");
        var palette = expected.GetProperty("palette").EnumerateArray()
            .Select(value => VanillaTerrainGeneration.ParseBlock(value.GetString()!))
            .ToArray();
        var indices = VanillaTerrainGeneration.Decode(expected.GetProperty("data").GetString()!);
        var order = entry.GetProperty("structureOrder").EnumerateArray()
            .Select(step => step.EnumerateArray().Select(id => id.GetString()!))
            .ToArray();
        var layout = pipeline.Structures!.Layout.StructuresPerStep;

        Assert.Equal(order.Length, layout.Count);

        for (var step = 0; step < order.Length; step++)
        {
            Assert.Equal(order[step], layout[step].Select(structure => structure.Identifier));
        }

        var terrain = new Dictionary<(int X, int Z), Chunk>();
        var box = start.BoundingBox;
        var index = 0;
        var mismatches = new List<string>();
        var chunkCount = 0;

        for (var chunkX = box.MinX >> 4; chunkX <= box.MaxX >> 4; chunkX++)
        {
            for (var chunkZ = box.MinZ >> 4; chunkZ <= box.MaxZ >> 4; chunkZ++)
            {
                // Like the capture, each chunk places into fresh terrain, without its neighbors' writes.
                var chunks = new Dictionary<(int X, int Z), IChunk>();

                for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
                {
                    for (var x = chunkX - 1; x <= chunkX + 1; x++)
                    {
                        if (!terrain.TryGetValue((x, z), out var generated))
                        {
                            generated = Terrain(pipeline, x, z);
                            terrain.Add((x, z), generated);
                        }

                        chunks.Add((x, z), generated.Clone(x, z));
                    }
                }

                pipeline.PlaceStructures(chunks, chunkX, chunkZ);
                chunkCount++;
                var chunk = chunks[(chunkX, chunkZ)];

                // The capture's boxes are those vanilla generated, which Obsidian's template pieces keep; its
                // scattered pieces settle on the terrain as they're placed, where vanilla's already are.
                foreach (var piece in start.Pieces)
                {
                    index = Compare(piece.BoundingBox, chunk, palette, indices, index, mismatches);
                }
            }
        }

        Assert.Equal(expected.GetProperty("chunks").GetInt32(), chunkCount);
        Assert.Equal(indices.Length, index);
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} blocks differ, first: "
            + string.Join("; ", mismatches.Take(12)));
    }

    // The piece box's blocks inside the chunk, in y, z, x order, against the next of the captured indices.
    private static int Compare(
        BlockBox pieceBox,
        IChunk chunk,
        IBlock[] palette,
        int[] indices,
        int index,
        List<string> mismatches)
    {
        var minX = Math.Max(pieceBox.MinX, chunk.X << 4);
        var maxX = Math.Min(pieceBox.MaxX, (chunk.X << 4) + 15);
        var minZ = Math.Max(pieceBox.MinZ, chunk.Z << 4);
        var maxZ = Math.Min(pieceBox.MaxZ, (chunk.Z << 4) + 15);
        var minY = Math.Max(pieceBox.MinY, chunk.MinY);
        var maxY = Math.Min(pieceBox.MaxY, chunk.MinY + chunk.Height - 1);

        for (var y = minY; y <= maxY; y++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var expected = index < indices.Length ? palette[indices[index]] : null;
                    var actual = chunk.GetBlock(x, y, z);
                    index++;

                    if (expected is null
                        || actual.RegistryId != expected.RegistryId
                        || actual.State?.Id != expected.State?.Id)
                    {
                        mismatches.Add($"({x}, {y}, {z}): expected {expected?.UnlocalizedName}, "
                            + $"actual {actual.UnlocalizedName}");
                    }
                }
            }
        }

        return index;
    }

    private static Chunk Terrain(BiomeTerrain pipeline, int chunkX, int chunkZ)
    {
        var chunk = new Chunk(chunkX, chunkZ, pipeline.MinY, pipeline.Height);
        pipeline.Generate(chunk);
        return chunk;
    }

    private static StructureStart Start(JsonElement entry, out BiomeTerrain pipeline)
    {
        var dimension = entry.GetProperty("dimension").GetString()!;
        var seed = entry.GetProperty("seed").GetInt64();
        pipeline = pipelines.GetOrAdd((dimension, seed), key => new Lazy<BiomeTerrain>(() => Pipeline(key))).Value;
        var structure = entry.GetProperty("structure").GetString()!;
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();

        return pipeline.Structures!.GetStarts(chunkX, chunkZ).Single(start => start.Structure.Identifier == structure);
    }

    private static BiomeTerrain Pipeline((string Dimension, long Seed) key)
    {
        var settings = key.Dimension switch
        {
            "minecraft:the_nether" => "minecraft:nether",
            "minecraft:the_end" => "minecraft:end",
            _ => key.Dimension
        };
        var preset = key.Dimension == "minecraft:the_nether" ? "minecraft:nether" : key.Dimension;
        var terrain = new TerrainGenerator(WorldgenData.Vanilla, key.Seed, settings);
        var registry = new FeatureRegistry(WorldgenData.Vanilla) { Template = VanillaFeatureTemplates.Get };

        return new BiomeTerrain(terrain, preset, registry, structures: true);
    }

    private static JsonElement Case(string name) =>
        VanillaWorldgenCaptures.CurrentCases("structures").Single(entry => entry.GetProperty("name").GetString() == name);

    private static TheoryData<string> Names(Func<JsonElement, bool> filter)
    {
        var names = new TheoryData<string>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("structures").Where(filter))
        {
            names.Add(entry.GetProperty("name").GetString()!);
        }

        return names;
    }
}
