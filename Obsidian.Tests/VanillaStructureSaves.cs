using Obsidian.Commands;
using Obsidian.Nbt;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData.Structures;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>Assets/structure_pieces.json</c>, what vanilla 26.3 saves of structure starts
/// (<c>StructureStart.createTag</c>), recorded by the asset generator's <c>StructurePieceFixtures.java</c>
/// (<c>docs/scenario-captures.md</c>), through the 26.3 pipeline: Obsidian saves each start as vanilla does, and
/// restores what vanilla saved once it placed it.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public class VanillaStructureSaves
{
    private static readonly JsonElement fixture = ReadFixture();

    // A start as generated saves like vanilla's; loading vanilla's save of it once placed (or, for starts recorded
    // only as generated, that save) restores its pieces' state, which Obsidian then saves the same way.
    [Fact]
    public void StartsSaveLikeVanilla()
    {
        var seed = fixture.GetProperty("seed").GetInt64();
        var cases = fixture.GetProperty("cases").EnumerateArray();

        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        WorldgenPipelines.AssertAllInSlices(cases, Name, (entry, pipelines) =>
        {
            var start = Start(entry, pipelines, seed);
            var fresh = entry.GetProperty("fresh");

            AssertSaved(fresh, start.SaveState());

            var loaded = entry.TryGetProperty("placed", out var placed) ? placed : fresh;
            start.RestoreState(Tag(loaded));

            AssertSaved(loaded, start.SaveState());
        });
    }

    // Saves of other pieces aren't restored; Obsidian's saves from before it wrote piece ids are.
    [Theory]
    [InlineData("minecraft:tesh", true)]
    [InlineData(null, true)]
    [InlineData("minecraft:tejp", false)]
    public void PieceStateIsRestoredForTheSamePieces(string? id, bool restored)
    {
        var entry = fixture.GetProperty("cases").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "swamp_hut");
        var start = Start(entry, new WorldgenPipelines(), fixture.GetProperty("seed").GetInt64());
        var saved = Tag(entry.GetProperty("placed"));
        var piece = (NbtCompound)((NbtList)saved["Children"])[0];

        piece.Remove("id");
        if (id is not null)
            piece.Add(new NbtTag<string>("id", id));

        start.RestoreState(saved);

        var expected = restored ? (NbtArray<int>)piece["BB"] : SaveBox(start.Pieces[0].BoundingBox);
        var actual = (NbtCompound)((NbtList)start.SaveState()["Children"])[0];
        Assert.Equal(Snbt.Write(expected), Snbt.Write(actual["BB"]));
    }

    private static StructureStart Start(JsonElement entry, WorldgenPipelines pipelines, long seed)
    {
        var pipeline = pipelines.Structures(entry.GetProperty("dimension").GetString()!, seed);
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

        start.Add(children);
        return start;
    }

    private static NbtArray<int> SaveBox(BlockBox box) =>
        new("BB", [box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ]);

    private static string Name(JsonElement entry) => entry.GetProperty("name").GetString()!;

    private static JsonElement ReadFixture()
    {
        using var stream = typeof(VanillaStructureSaves).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.structure_pieces.json")!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.Clone();
    }
}
