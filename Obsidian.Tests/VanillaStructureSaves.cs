using Obsidian.API;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands;
using Obsidian.Nbt;
using Obsidian.Tests.Utilities;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using Obsidian.WorldData.Structures.Templates;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>Assets/structure_pieces.json</c>, what vanilla 26.3 saves of structure starts
/// (<c>StructureStart.createTag</c>), recorded by the asset generator's <c>StructurePieceFixtures.java</c>
/// (<c>docs/scenario-captures.md</c>), through the 26.3 pipeline: Obsidian saves each start as vanilla does, takes
/// vanilla's saves of it for its own, restores what vanilla saved once it placed it, and once it places it itself,
/// saves what vanilla saved.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public class VanillaStructureSaves
{
    private static readonly JsonElement fixture = ReadFixture();

    // A start as generated saves like vanilla's, and accepts vanilla's saves of it: as generated, once placed and
    // once vanilla loaded it again. Restoring vanilla's last save before loading (placed, or else as generated)
    // gives the pieces' state back, which Obsidian saves the same way; and a start Obsidian places in every chunk its
    // box reaches, in the capture's order, saves what vanilla saved once it placed it.
    [Fact]
    public void StartsSaveLikeVanilla()
    {
        var seed = fixture.GetProperty("seed").GetInt64();
        var cases = fixture.GetProperty("cases").EnumerateArray();

        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        WorldgenPipelines.AssertAllInSlices(cases, Name, (entry, pipelines) =>
        {
            var start = Start(entry, pipelines.Structures(Dimension(entry), seed));
            var fresh = entry.GetProperty("fresh");
            var hasPlaced = entry.TryGetProperty("placed", out var placed);

            AssertSaved(fresh, start.SaveState());

            foreach (var save in new[] { "fresh", "placed", "reloaded" })
            {
                if (entry.TryGetProperty(save, out var saved))
                    Assert.True(start.Accepts(Tag(saved)), $"Vanilla's {save} save is rejected.");
            }

            var loaded = hasPlaced ? placed : fresh;
            start.RestoreState(Tag(loaded));

            AssertSaved(loaded, start.SaveState());

            if (!hasPlaced)
                return;

            var pipeline = new WorldgenPipelines().Structures(Dimension(entry), seed);
            var placing = Start(entry, pipeline);

            PlaceEveryChunk(pipeline, placing);

            AssertSaved(placed, placing.SaveState());
        });
    }

    // Vanilla's save of the placed swamp hut, changed so that it's of other pieces, is rejected: the chunk saves it
    // again as it was, and the start isn't placed nor takes references. Obsidian's older saves, without piece ids,
    // are restored.
    [Theory]
    [InlineData("as saved", true)]
    [InlineData("without ids", true)]
    [InlineData("unknown id", false)]
    [InlineData("other orientation", false)]
    [InlineData("other pieces", false)]
    public void SavesOfOtherPiecesAreKeptAndUnused(string change, bool restored)
    {
        var entry = fixture.GetProperty("cases").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "swamp_hut");
        var pipeline = new WorldgenPipelines().Structures(Dimension(entry), fixture.GetProperty("seed").GetInt64());
        var start = Start(entry, pipeline);
        var generated = start.Pieces[0].BoundingBox;
        var saved = Tag(entry.GetProperty("placed"));
        var pieces = (NbtList)saved["Children"];
        var piece = (NbtCompound)pieces[0];

        switch (change)
        {
            case "without ids":
                piece.Remove("id");
                break;
            case "unknown id":
                piece.Remove("id");
                piece.Add(new NbtTag<string>("id", "minecraft:unknown_piece"));
                break;
            case "other orientation":
                piece.Remove("O");
                piece.Add(new NbtTag<int>("O", 1));
                break;
            case "other pieces":
                pieces.Add(SnbtReader.ReadCompound(Pieces(entry.GetProperty("placed"))[0]));
                break;
        }

        var expected = Snbt.Write(saved);
        var starts = new NbtCompound("starts") { saved };

        StructureManager.RestoreStart(start, starts);

        var resaved = pipeline.Structures!.SaveStarts(start.ChunkX, start.ChunkZ, starts)!;
        var resavedStart = (NbtCompound)resaved[start.Structure.Identifier];

        Assert.Equal(!restored, start.IsRejected);

        if (restored)
        {
            AssertSaved(entry.GetProperty("placed"), resavedStart);
            return;
        }

        Assert.Same(saved, resavedStart);
        Assert.Equal(expected, Snbt.Write(resavedStart));
        Assert.False(start.TryAddReference());

        var terrain = Area(pipeline, start.ChunkX, start.ChunkZ);
        var placed = Area(pipeline, start.ChunkX, start.ChunkZ);

        pipeline.PlaceStructures(placed, start.ChunkX, start.ChunkZ);

        Assert.Equal(generated, start.Pieces[0].BoundingBox);
        Assert.Equal(Blocks(terrain, generated), Blocks(placed, generated));
    }

    // A feature element saves its placed feature by identifier, or, defined inline, as its definition (no vanilla
    // pool defines one inline), which vanilla's codec reads like its own.
    [Theory]
    [InlineData("\"minecraft:oak\"", "\"minecraft:oak\"")]
    [InlineData(
        "{\"feature\": \"minecraft:oak\", \"placement\": [{\"type\": \"minecraft:count\", \"count\": 2}]}",
        "{feature: \"minecraft:oak\", placement: [{count: 2, type: \"minecraft:count\"}]}")]
    [InlineData(
        "{\"feature\": {\"type\": \"minecraft:no_op\", \"config\": {}}, \"placement\": []}",
        "{feature: {config: {}, type: \"minecraft:no_op\"}, placement: []}")]
    public void FeatureElementsSaveTheirFeature(string feature, string saved)
    {
        var element = FeatureElement(feature);

        var expected = $"{{element_type: \"minecraft:feature_pool_element\", feature: {saved}, projection: \"rigid\"}}";
        Assert.Equal(expected, Snbt.Write(element.Save(null)));
    }

    // A jigsaw piece with an inline placed feature is the piece vanilla saved with the feature as its codec writes it
    // (vanilla's save, recorded from its StructurePoolElement codec, leaves the empty config out); the rest of the
    // element still has to match.
    [Theory]
    [InlineData(
        "{element_type:\"minecraft:feature_pool_element\",feature:{feature:{type:\"minecraft:no_op\"},placement:[]},"
        + "projection:\"rigid\"}",
        true)]
    [InlineData(
        "{element_type:\"minecraft:feature_pool_element\",feature:{feature:{type:\"minecraft:no_op\"},placement:[]},"
        + "projection:\"terrain_matching\"}",
        false)]
    [InlineData(
        "{element_type:\"minecraft:feature_pool_element\",feature:\"minecraft:oak\",projection:\"rigid\"}",
        false)]
    public void InlineFeaturesMatchVanillasSaves(string element, bool matches)
    {
        var feature = FeatureElement(
            "{\"feature\": {\"type\": \"minecraft:no_op\", \"config\": {}}, \"placement\": []}");
        var position = new Vector(0, 64, 0);
        var box = new BlockBox(position, position);
        var piece = new PoolElementStructurePiece(
            feature, position, 1, StructureRotation.None, box, LiquidSettings.ApplyWaterlogging);
        var saved = new NbtCompound();
        var vanilla = SnbtReader.ReadCompound(element);

        piece.SaveState(saved);
        saved.Remove("pool_element");
        vanilla.Name = "pool_element";
        saved.Add(vanilla);

        Assert.Equal(matches, piece.IsSavedAs(saved));
    }

    // A ship too big for a decoration region (vanilla's are all small enough, so it's a 33 by 20 by 15 template) gets
    // its height as the structure is built, which counts as adjusted; others don't have one until placed.
    [Theory]
    [InlineData(true, 40, true)]
    [InlineData(false, 90, false)]
    public void OversizedShipwrecksSaveTheirHeightAsAdjusted(bool oversized, int height, bool adjusted)
    {
        var oversizedTemplate = EmptyTemplate(new Vector(33, 20, 15));
        using var templates = StructureRegistry.Use(oversized ? _ => oversizedTemplate : VanillaFeatureTemplates.Get);
        var random = new LegacyRandomSource(0L);
        var piece = ShipwreckPiece.AddRandomPiece(
            new Vector(0, 90, 0), StructureRotation.None, new StructurePiecesBuilder(), random, false);

        Assert.Equal(oversized, piece.IsTooBigToFitInWorldGenRegion);

        if (oversized)
            piece.AdjustPositionHeight(height);

        var saved = new NbtCompound();
        piece.SaveState(saved);

        Assert.Equal(height, saved.GetInt("TPY"));
        Assert.True(saved.TryGetBool("height_adjusted", out var flag));
        Assert.Equal(adjusted, flag);
    }

    // A template of that size without blocks, read from vanilla's template format.
    private static StructureTemplate EmptyTemplate(Vector size)
    {
        using var stream = new MemoryStream();

        using (var writer = new NbtWriterStream(stream, NbtCompression.None, ""))
        {
            writer.WriteTag(new NbtList(NbtTagType.Int, "size")
            {
                new NbtTag<int>(string.Empty, size.X),
                new NbtTag<int>(string.Empty, size.Y),
                new NbtTag<int>(string.Empty, size.Z)
            });
            writer.WriteTag(new NbtList(NbtTagType.Compound, "palette"));
            writer.WriteTag(new NbtList(NbtTagType.Compound, "blocks"));
            writer.WriteTag(new NbtList(NbtTagType.Compound, "entities"));
            writer.EndCompound();
            writer.TryFinish();
        }

        return StructureTemplate.Load(new MemoryStream(stream.ToArray()), NbtCompression.None);
    }

    private static StructurePoolElement FeatureElement(string feature)
    {
        var features = new FeatureRegistry(WorldgenData.Vanilla);
        var reader = new WorldgenReader(
            WorldgenTypes.BuiltIn,
            id => features.ConfiguredAdapter(id),
            id => features.PlacedAdapter(id));
        var json = $$"""
            {"element_type": "minecraft:feature_pool_element", "projection": "rigid", "feature": {{feature}}}
            """;

        using var document = JsonDocument.Parse(json);
        return reader.Read<StructurePoolElement>(document.RootElement);
    }

    // Decorates each chunk the start's box reaches, in x then z order like the capture, in one world: terrain is
    // generated as decoration needs it, and each chunk sees what its decorated neighbors wrote.
    private static void PlaceEveryChunk(BiomeTerrain pipeline, StructureStart start)
    {
        var chunks = new Dictionary<(int X, int Z), IChunk>();
        var box = start.BoundingBox;

        for (var chunkX = box.MinX >> 4; chunkX <= box.MaxX >> 4; chunkX++)
        {
            for (var chunkZ = box.MinZ >> 4; chunkZ <= box.MaxZ >> 4; chunkZ++)
            {
                AddTerrain(pipeline, chunks, chunkX, chunkZ);
                pipeline.Decorate(chunks, chunkX, chunkZ);
            }
        }
    }

    // A chunk and its neighbors at the terrain status.
    private static Dictionary<(int X, int Z), IChunk> Area(BiomeTerrain pipeline, int chunkX, int chunkZ)
    {
        var chunks = new Dictionary<(int X, int Z), IChunk>();
        AddTerrain(pipeline, chunks, chunkX, chunkZ);
        return chunks;
    }

    private static void AddTerrain(
        BiomeTerrain pipeline, Dictionary<(int X, int Z), IChunk> chunks, int chunkX, int chunkZ)
    {
        for (var x = chunkX - 1; x <= chunkX + 1; x++)
        {
            for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
            {
                if (chunks.ContainsKey((x, z)))
                    continue;

                var chunk = new Chunk(x, z, pipeline.MinY, pipeline.Height);
                pipeline.Generate(chunk);
                chunks.Add((x, z), chunk);
            }
        }
    }

    // The blocks of the box the chunks hold.
    private static List<string> Blocks(Dictionary<(int X, int Z), IChunk> chunks, BlockBox box)
    {
        var blocks = new List<string>();

        for (var x = box.MinX; x <= box.MaxX; x++)
        {
            for (var z = box.MinZ; z <= box.MaxZ; z++)
            {
                if (!chunks.TryGetValue((x >> 4, z >> 4), out var chunk))
                    continue;

                for (var y = box.MinY; y <= box.MaxY; y++)
                    blocks.Add(chunk.GetBlock(x, y, z).ToString()!);
            }
        }

        return blocks;
    }

    private static string Dimension(JsonElement entry) => entry.GetProperty("dimension").GetString()!;

    private static string[] Pieces(JsonElement saved) =>
        [.. saved.GetProperty("pieces").EnumerateArray().Select(piece => piece.GetString()!)];

    private static StructureStart Start(JsonElement entry, BiomeTerrain pipeline)
    {
        var structure = entry.GetProperty("structure").GetString();
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();

        return pipeline.Structures!.GetStarts(chunkX, chunkZ).Single(start => start.Structure.Identifier == structure);
    }

    // The start and each of its pieces, so that a difference names its piece.
    private static void AssertSaved(JsonElement expected, NbtCompound actual)
    {
        var tag = Tag(expected);

        Assert.Equal(Snbt.Write(Head(tag)), Snbt.Write(Head(actual)));

        var expectedPieces = (NbtList)tag["Children"];
        var actualPieces = (NbtList)actual["Children"];

        Assert.Equal(expectedPieces.Count, actualPieces.Count);

        for (var i = 0; i < expectedPieces.Count; i++)
        {
            var vanilla = Snbt.Write(expectedPieces[i]);
            var obsidian = Snbt.Write(actualPieces[i]);
            if (vanilla != obsidian)
                throw new XunitException($"Piece {i} differs.\nVanilla:  {vanilla}\nObsidian: {obsidian}");
        }
    }

    // The start without its pieces.
    private static NbtCompound Head(NbtCompound start)
    {
        var head = new NbtCompound();
        foreach (var (name, tag) in start)
        {
            if (name != "Children")
                head.Add(name, tag);
        }

        return head;
    }

    private static NbtCompound Tag(JsonElement saved)
    {
        var start = SnbtReader.ReadCompound(saved.GetProperty("start").GetString()!);
        var children = new NbtList(NbtTagType.Compound, "Children");
        foreach (var piece in saved.GetProperty("pieces").EnumerateArray())
            children.Add(SnbtReader.ReadCompound(piece.GetString()!));

        // Named like the chunk's structures.starts entry it would be.
        start.Name = start.GetString("id");
        start.Add(children);
        return start;
    }

    private static string Name(JsonElement entry) => entry.GetProperty("name").GetString()!;

    private static JsonElement ReadFixture()
    {
        using var stream = typeof(VanillaStructureSaves).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.structure_pieces.json")!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.Clone();
    }
}
