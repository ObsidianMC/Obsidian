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
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for structures, replaying the references in <c>Assets/vanilla_worldgen.json</c>
/// (<see cref="VanillaWorldgenCaptures"/>): vanilla's <c>Structure.generate</c> piece lists and blocks of vanilla server
/// worlds with the same seeds. Re-record them with <c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen</c>
/// (<c>docs/vanilla-captures.md</c>).
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(WorldgenCollection.Name)]
public class VanillaStructureGeneration(WorldgenFixture worldgen)
{
    // SHA-256 of the piece list (one line per piece: type, box, pool element or template, position, rotation...).
    public static TheoryData<string, long, string, int, int, int, string> PieceCases()
    {
        var cases = new TheoryData<string, long, string, int, int, int, string>();
        foreach (var entry in VanillaWorldgenCaptures.Cases("structurePieces"))
        {
            cases.Add(entry.GetProperty("dimension").GetString()!, entry.GetProperty("seed").GetInt64(), entry.GetProperty("structure").GetString()!,
                entry.GetProperty("chunkX").GetInt32(), entry.GetProperty("chunkZ").GetInt32(), entry.GetProperty("pieceCount").GetInt32(),
                entry.GetProperty("pieces").GetString()!);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(PieceCases))]
    public void PiecesMatchVanilla(string dimensionName, long seed, string structure, int chunkX, int chunkZ, int pieceCount, string expected)
    {
        var start = worldgen.Builder(Dimension(dimensionName), seed).Structures!.GetStarts(chunkX, chunkZ)
            .Single(start => start.Structure.Identifier == structure);

        Assert.Equal(pieceCount, start.Pieces.Count);
        Assert.Equal(expected, Sha256(string.Join('\n', start.Pieces.Select(Describe))));
    }

    // SHA-256 of the block state ids (little-endian uint16) inside each piece box, in piece order and y, z, x order, in full
    // chunks of a vanilla server world.
    public static TheoryData<string, long, string, int, int, string> BlockCases()
    {
        var cases = new TheoryData<string, long, string, int, int, string>();
        foreach (var entry in VanillaWorldgenCaptures.Cases("structureBlocks"))
        {
            cases.Add(entry.GetProperty("dimension").GetString()!, entry.GetProperty("seed").GetInt64(), entry.GetProperty("structure").GetString()!,
                entry.GetProperty("chunkX").GetInt32(), entry.GetProperty("chunkZ").GetInt32(), entry.GetProperty("blocks").GetString()!);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(BlockCases))]
    public void BlocksMatchVanilla(string dimensionName, long seed, string structure, int chunkX, int chunkZ, string expected)
    {
        var dimension = Dimension(dimensionName);
        var chunks = worldgen.Chunks(dimension, seed);
        var start = worldgen.Builder(dimension, seed).Structures!.GetStarts(chunkX, chunkZ).Single(start => start.Structure.Identifier == structure);
        var box = start.BoundingBox;

        // Every chunk the structure reaches is completed before reading, since pieces are placed by each chunk they reach.
        for (var x = box.MinX >> 4; x <= box.MaxX >> 4; x++)
        {
            for (var z = box.MinZ >> 4; z <= box.MaxZ >> 4; z++)
                chunks.Get(x, z);
        }

        var ids = new List<byte>();
        foreach (var piece in start.Pieces)
        {
            var pieceBox = piece.BoundingBox;
            for (var y = Math.Max(pieceBox.MinY, dimension.MinY); y <= Math.Min(pieceBox.MaxY, dimension.MinY + dimension.Height - 1); y++)
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

        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(ids.ToArray())));
    }

    // Vanilla: /loot spawn x y z loot minecraft:chests/shipwreck_map, whose exploration_map function finds the nearest
    // structure with the tag, then the center and colors (SHA-256) of the map's saved data.
    public static IEnumerable<object[]> TreasureMapCases() => VanillaWorldgenCaptures.Cases("treasureMaps").Select(entry => new object[]
    {
        entry.GetProperty("seed").GetInt64(), entry.GetProperty("tag").GetString()!, entry.GetProperty("x").GetInt32(),
        entry.GetProperty("y").GetInt32(), entry.GetProperty("z").GetInt32(), entry.GetProperty("radius").GetInt32(),
        entry.GetProperty("skipKnown").GetBoolean(), entry.GetProperty("zoom").GetByte(), entry.GetProperty("targetX").GetInt32(),
        entry.GetProperty("targetY").GetInt32(), entry.GetProperty("targetZ").GetInt32(), entry.GetProperty("centerX").GetInt32(),
        entry.GetProperty("centerZ").GetInt32(), entry.GetProperty("colors").GetString()!
    });

    [Theory]
    [MemberData(nameof(TreasureMapCases))]
    public void TreasureMapMatchesVanilla(long seed, string tag, int x, int y, int z, int radius, bool skipKnown, byte zoom,
        int targetX, int targetY, int targetZ, int centerX, int centerZ, string expectedColors)
    {
        var builder = worldgen.Builder(MojangDimension.Overworld, seed);
        var target = new StructureLocator(builder.Structures!).FindNearest(tag, new Vector(x, y, z), radius, skipKnown);
        Assert.Equal(new Vector(targetX, targetY, targetZ), target);

        var map = MapData.CreateFresh(target!.Value.X, target.Value.Z, zoom, true, true, "minecraft:overworld");
        MapRenderer.RenderBiomePreview(map, (x, z) => builder.GetBiome(x, 0, z));

        Assert.Equal((centerX, centerZ), (map.CenterX, map.CenterZ));
        Assert.Equal(expectedColors, Convert.ToHexStringLower(SHA256.HashData(map.Colors)));
    }

    private static MojangDimension Dimension(string name) => name switch
    {
        "nether" => MojangDimension.Nether,
        "end" => MojangDimension.End,
        _ => MojangDimension.Overworld
    };

    private static string Describe(StructurePiece piece)
    {
        static string Box(BlockBox box) => $"{box.MinX},{box.MinY},{box.MinZ},{box.MaxX},{box.MaxY},{box.MaxZ}";
        static string Position(Vector position) => $"{position.X},{position.Y},{position.Z}";

        var parts = new List<string> { piece.GetType().Name, "box " + Box(piece.BoundingBox) };
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
