using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.WorldData.Structures.Templates;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Replays the 26.3 <c>structures</c> references (<c>docs/vanilla-captures.md</c>) through the 26.3 pipeline: the
/// piece list of a start, and for some starts the blocks their pieces hold once structures alone are placed in every
/// chunk they reach, on terrain bearded by them.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public class VanillaStructurePlacement
{
    // Every start's pieces, and the blocks of the starts the capture recorded them for.
    [Fact]
    public void StructuresMatchVanilla()
    {
        var cases = VanillaWorldgenCaptures.CurrentCases("structures");

        WorldgenPipelines.AssertAllInSlices(cases, Name, AssertStructure);
    }

    private static string Name(JsonElement entry) => entry.GetProperty("name").GetString()!;

    private static void AssertStructure(JsonElement entry, WorldgenPipelines pipelines)
    {
        var dimension = entry.GetProperty("dimension").GetString()!;
        var pipeline = pipelines.Structures(dimension, entry.GetProperty("seed").GetInt64());
        var start = Start(entry, pipeline);

        AssertPieces(entry, start);

        if (entry.TryGetProperty("blocks", out var blocks))
            AssertBlocks(entry, blocks, start, pipeline);
    }

    private static void AssertPieces(JsonElement entry, StructureStart start)
    {
        var lines = start.Pieces.Select(Describe);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));

        Assert.Equal(entry.GetProperty("pieceCount").GetInt32(), start.Pieces.Count);
        Assert.Equal(entry.GetProperty("pieces").GetString(), hash);
    }

    private static void AssertBlocks(
        JsonElement entry, JsonElement expected, StructureStart start, BiomeTerrain pipeline)
    {
        var palette = expected.GetProperty("palette").EnumerateArray()
            .Select(value => VanillaTerrainGeneration.ParseBlock(value.GetString()!))
            .ToArray();
        var indices = VanillaTerrainGeneration.Decode(expected.GetProperty("data").GetString()!);
        var bounds = expected.GetProperty("bounds").EnumerateArray()
            .Select(values => values.EnumerateArray().Select(value => value.GetInt32()).ToArray())
            .Select(values => new BlockBox(
                new Vector(values[0], values[1], values[2]),
                new Vector(values[3], values[4], values[5])))
            .ToArray();
        var sampled = expected.GetProperty("chunkList").EnumerateArray()
            .Select(values => (X: values[0].GetInt32(), Z: values[1].GetInt32()))
            .ToArray();
        var comparison = new Comparison(palette, indices);

        AssertStructureOrder(entry, pipeline);
        Assert.Equal(start.Pieces.Count, bounds.Length);

        PlaceAndCompare(pipeline, sampled, bounds, comparison);

        var mismatches = comparison.Mismatches;

        Assert.Equal(expected.GetProperty("chunks").GetInt32(), sampled.Length);
        Assert.Equal(indices.Length, comparison.Index);
        // Bounds of terrain alone would prove nothing: vanilla's blocks there have to show the structure.
        Assert.True(comparison.StructureBlocks > 0, "The captured bounds hold no structure blocks.");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} blocks differ, first: "
            + string.Join("; ", mismatches.Take(12)));
    }

    private static void AssertStructureOrder(JsonElement entry, BiomeTerrain pipeline)
    {
        var order = entry.GetProperty("structureOrder").EnumerateArray()
            .Select(step => step.EnumerateArray().Select(id => id.GetString()!))
            .ToArray();
        var layout = pipeline.Structures!.Layout.StructuresPerStep;

        Assert.Equal(order.Length, layout.Count);

        for (var step = 0; step < order.Length; step++)
            Assert.Equal(order[step], layout[step].Select(structure => structure.Identifier));
    }

    // In the capture's order, one at a time: the first chunk a shipwreck reaches picks its height. Like the capture,
    // each chunk places into fresh terrain, without its neighbors' writes; the terrain is generated once.
    private static void PlaceAndCompare(
        BiomeTerrain pipeline, (int X, int Z)[] sampled, BlockBox[] bounds, Comparison comparison)
    {
        var terrain = new Dictionary<(int X, int Z), Chunk>();

        foreach (var (chunkX, chunkZ) in sampled)
        {
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

            foreach (var pieceBounds in bounds)
                comparison.Compare(pieceBounds, chunks[(chunkX, chunkZ)], terrain[(chunkX, chunkZ)]);
        }
    }

    // A shipwreck continues at the height vanilla saved (height_adjusted and the template position's TPY), and
    // reads the height Obsidian saved before it wrote those.
    [Theory]
    [InlineData("vanilla", 58)]
    [InlineData("obsidian", 57)]
    public void ShipwreckKeepsSavedHeight(string format, int height)
    {
        using var templates = Registries.StructureRegistry.Use(VanillaFeatureTemplates.Get);
        var pieces = new StructurePiecesBuilder();
        var random = new API.World.Generator.RandomSources.LegacyRandomSource(0L);
        var piece = ShipwreckPiece.AddRandomPiece(new Vector(0, 90, 0), StructureRotation.None, pieces, random, true);
        var saved = new NbtCompound();

        if (format == "vanilla")
        {
            saved.Add(new NbtTag<bool>("height_adjusted", true));
            saved.Add(new NbtTag<int>("TPY", height));
        }
        else
        {
            saved.Add(new NbtTag<int>("adjusted_y", height));
        }

        piece.LoadState(saved);
        var resaved = new NbtCompound();
        piece.SaveState(resaved);

        Assert.True(resaved.TryGetTag<NbtTag<bool>>("height_adjusted", out var adjusted) && adjusted.Value);
        Assert.Equal(height, resaved.GetInt("TPY"));
    }

    // The captured blocks, in order, against the bounds' blocks inside each chunk in y, z, x order.
    private sealed class Comparison(IBlock[] palette, int[] indices)
    {
        public int Index { get; private set; }

        public List<string> Mismatches { get; } = [];

        // Captured blocks that differ from Obsidian's terrain alone: those the structure placed.
        public int StructureBlocks { get; private set; }

        public void Compare(BlockBox bounds, IChunk chunk, IChunk terrain)
        {
            var minX = Math.Max(bounds.MinX, chunk.X << 4);
            var maxX = Math.Min(bounds.MaxX, (chunk.X << 4) + 15);
            var minZ = Math.Max(bounds.MinZ, chunk.Z << 4);
            var maxZ = Math.Min(bounds.MaxZ, (chunk.Z << 4) + 15);
            var minY = Math.Max(bounds.MinY, chunk.MinY);
            var maxY = Math.Min(bounds.MaxY, chunk.MinY + chunk.Height - 1);

            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        var expected = this.Index < indices.Length ? palette[indices[this.Index]] : null;
                        var actual = chunk.GetBlock(x, y, z);
                        this.Index++;

                        if (expected is not null && !Same(expected, terrain.GetBlock(x, y, z)))
                        {
                            this.StructureBlocks++;
                        }

                        if (expected is null || !Same(expected, actual))
                        {
                            this.Mismatches.Add($"({x}, {y}, {z}): expected {expected?.UnlocalizedName}, "
                                + $"actual {actual.UnlocalizedName}");
                        }
                    }
                }
            }
        }

        private static bool Same(IBlock expected, IBlock actual) =>
            actual.RegistryId == expected.RegistryId && actual.State?.Id == expected.State?.Id;
    }

    private static Chunk Terrain(BiomeTerrain pipeline, int chunkX, int chunkZ)
    {
        var chunk = new Chunk(chunkX, chunkZ, pipeline.MinY, pipeline.Height);
        pipeline.Generate(chunk);
        return chunk;
    }

    private static StructureStart Start(JsonElement entry, BiomeTerrain pipeline)
    {
        var structure = entry.GetProperty("structure").GetString()!;
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();

        return pipeline.Structures!.GetStarts(chunkX, chunkZ).Single(start => start.Structure.Identifier == structure);
    }

    // A piece as the references describe it: its type, box and what places it.
    private static string Describe(StructurePiece piece)
    {
        static string Box(BlockBox box) => $"{box.MinX},{box.MinY},{box.MinZ},{box.MaxX},{box.MaxY},{box.MaxZ}";
        static string Position(Vector position) => $"{position.X},{position.Y},{position.Z}";

        // Vanilla's generated fortress start keeps BridgeCrossing's piece type (NeBCr), which the references name.
        var type = piece is NetherFortressPieces.StartPiece ? "BridgeCrossing" : piece.GetType().Name;
        var parts = new List<string> { type, "box " + Box(piece.BoundingBox) };
        switch (piece)
        {
            case PoolElementStructurePiece pool:
                parts.Add(DescribeElement(pool.Element));
                parts.Add(pool.Element.Projection == Projection.Rigid ? "rigid" : "terrain_matching");
                parts.Add("pos " + Position(pool.Position));
                parts.Add(Rotation(pool.ElementRotation));
                parts.Add("ground " + pool.GroundLevelDelta);
                parts.Add("junctions " + string.Join(";", pool.Junctions.Select(junction =>
                    $"{junction.SourceX},{junction.SourceGroundY},{junction.SourceZ},{junction.DeltaY},{(junction.DestinationRigid ? 1 : 0)}")));
                break;
            case TemplateStructurePiece template:
                parts.Add(template.TemplateName);
                parts.Add("pos " + Position(template.TemplatePosition));
                parts.Add(Rotation(template.PlaceSettings.Rotation));
                parts.Add(template.PlaceSettings.Mirror switch
                {
                    StructureMirror.LeftRight => "LEFT_RIGHT",
                    StructureMirror.FrontBack => "FRONT_BACK",
                    _ => "NONE"
                });
                parts.Add("pivot " + Position(template.PlaceSettings.RotationPivot));
                break;
            default:
                parts.Add(piece.Orientation?.ToString().ToLowerInvariant() ?? "none");
                parts.Add("depth " + piece.GenDepth);
                break;
        }

        return string.Join(" | ", parts);
    }

    private static string DescribeElement(StructurePoolElement element) => element switch
    {
        LegacySinglePoolElement legacy => "legacy:" + legacy.Location,
        SinglePoolElement single => "single:" + single.Location,
        ListPoolElement list => "list:[" + string.Join(",", list.Elements.Select(DescribeElement)) + "]",
        FeaturePoolElement feature => "feature:" + feature.Feature.Identifier,
        _ => "empty"
    };

    private static string Rotation(StructureRotation rotation) => rotation switch
    {
        StructureRotation.Clockwise90 => "CLOCKWISE_90",
        StructureRotation.Clockwise180 => "CLOCKWISE_180",
        StructureRotation.CounterClockwise90 => "COUNTERCLOCKWISE_90",
        _ => "NONE"
    };
}
