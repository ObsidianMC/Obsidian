using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.Registries;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Maps;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for structures, replaying the references in <c>Assets/vanilla_worldgen.json</c>
/// (<see cref="VanillaWorldgenCaptures"/>): vanilla's <c>Structure.generate</c> piece lists and blocks of vanilla server
/// worlds with the same seeds. They are 1.21.11 references of the legacy generator, which can't be re-recorded on 26.3;
/// Phase 5 replaces them with 26.3 captures (<c>docs/vanilla-captures.md</c>).
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(WorldgenCollection.Name)]
public class VanillaStructureGeneration(WorldgenFixture worldgen)
{
    // SHA-256 of the piece list (one line per piece: type, box, pool element or template, position, rotation...).
    [Fact]
    public void PiecesMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("structurePieces"), StructureName, this.AssertPieces);

    private void AssertPieces(JsonElement entry)
    {
        var start = this.Start(entry);

        Assert.Equal(entry.GetProperty("pieceCount").GetInt32(), start.Pieces.Count);
        Assert.Equal(entry.GetProperty("pieces").GetString(), Sha256(string.Join('\n', start.Pieces.Select(Describe))));
    }

    // SHA-256 of the block state ids (little-endian uint16) inside each piece box, in piece order and y, z, x order, in
    // full chunks of a vanilla server world.
    [Fact]
    public void BlocksMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("structureBlocks"), StructureName, this.AssertBlocks);

    private void AssertBlocks(JsonElement entry)
    {
        var dimension = Dimension(entry.GetProperty("dimension").GetString()!);
        var chunks = worldgen.Chunks(dimension, entry.GetProperty("seed").GetInt64());
        var start = this.Start(entry);
        var box = start.BoundingBox;

        // Every chunk the structure reaches is completed before reading, since pieces are placed by each chunk they reach.
        for (var x = box.MinX >> 4; x <= box.MaxX >> 4; x++)
        {
            for (var z = box.MinZ >> 4; z <= box.MaxZ >> 4; z++)
                chunks.Get(x, z);
        }

        var ids = new List<byte>();
        foreach (var piece in start.Pieces)
            AddStateIds(ids, piece.BoundingBox, dimension, chunks);

        Assert.Equal(entry.GetProperty("blocks").GetString(), Convert.ToHexStringLower(SHA256.HashData(ids.ToArray())));
    }

    private static void AddStateIds(List<byte> ids, BlockBox pieceBox, MojangDimension dimension, FullChunks chunks)
    {
        var minY = Math.Max(pieceBox.MinY, dimension.MinY);
        var maxY = Math.Min(pieceBox.MaxY, dimension.MinY + dimension.Height - 1);

        for (var y = minY; y <= maxY; y++)
        {
            for (var z = pieceBox.MinZ; z <= pieceBox.MaxZ; z++)
            {
                for (var x = pieceBox.MinX; x <= pieceBox.MaxX; x++)
                {
                    var id = LegacyWorldgenBlockStates.StateId(chunks.Get(x >> 4, z >> 4).GetBlock(x, y, z));
                    ids.Add((byte)id);
                    ids.Add((byte)(id >> 8));
                }
            }
        }
    }

    // Vanilla: /loot spawn x y z loot minecraft:chests/shipwreck_map, whose exploration_map function finds the nearest
    // structure with the tag, then the center and colors (SHA-256) of the map's saved data.
    [Fact]
    public void TreasureMapsMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("treasureMaps"), MapName, this.AssertTreasureMap);

    private void AssertTreasureMap(JsonElement entry)
    {
        var builder = worldgen.Builder(MojangDimension.Overworld, entry.GetProperty("seed").GetInt64());
        var origin = Position(entry, "x", "y", "z");
        var radius = entry.GetProperty("radius").GetInt32();
        var skipKnown = entry.GetProperty("skipKnown").GetBoolean();

        var target = new StructureLocator(builder.Structures!).FindNearest(Tag(entry), origin, radius, skipKnown);

        Assert.Equal(Position(entry, "targetX", "targetY", "targetZ"), target);

        var zoom = entry.GetProperty("zoom").GetByte();
        var map = MapData.CreateFresh(target!.Value.X, target.Value.Z, zoom, true, true, "minecraft:overworld");
        MapRenderer.RenderBiomePreview(map, (x, z) => builder.GetBiome(x, 0, z));

        var center = (entry.GetProperty("centerX").GetInt32(), entry.GetProperty("centerZ").GetInt32());
        Assert.Equal(center, (map.CenterX, map.CenterZ));
        Assert.Equal(entry.GetProperty("colors").GetString(), Convert.ToHexStringLower(SHA256.HashData(map.Colors)));
    }

    private StructureStart Start(JsonElement entry)
    {
        var dimension = Dimension(entry.GetProperty("dimension").GetString()!);
        var builder = worldgen.Builder(dimension, entry.GetProperty("seed").GetInt64());
        var structure = entry.GetProperty("structure").GetString()!;
        var chunkX = entry.GetProperty("chunkX").GetInt32();
        var chunkZ = entry.GetProperty("chunkZ").GetInt32();
        var starts = builder.Structures!.GetStarts(chunkX, chunkZ);

        return starts.Single(start => start.Structure.Identifier == structure);
    }

    private static string Tag(JsonElement entry) => entry.GetProperty("tag").GetString()!;

    private static Vector Position(JsonElement entry, string x, string y, string z) =>
        new(entry.GetProperty(x).GetInt32(), entry.GetProperty(y).GetInt32(), entry.GetProperty(z).GetInt32());

    private static string StructureName(JsonElement entry) =>
        $"{entry.GetProperty("structure").GetString()} in the {entry.GetProperty("dimension").GetString()} "
        + $"of seed {entry.GetProperty("seed")}";

    private static string MapName(JsonElement entry) => $"{Tag(entry)} map of seed {entry.GetProperty("seed")}";

    private static MojangDimension Dimension(string name) => name switch
    {
        "nether" => MojangDimension.Nether,
        "end" => MojangDimension.End,
        _ => MojangDimension.Overworld
    };

    internal static string Describe(StructurePiece piece)
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

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
