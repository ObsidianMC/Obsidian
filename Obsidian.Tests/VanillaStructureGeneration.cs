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
/// Parity checks for structures. Expected values come from running vanilla 1.21.11 with the same seeds:
/// <c>Structure.generate</c> piece lists, and blocks of real vanilla server worlds.
/// </summary>
[Collection(WorldgenCollection.Name)]
public class VanillaStructureGeneration(WorldgenFixture worldgen)
{
    [Theory]
    // SHA-256 of the piece list (one line per piece: type, box, pool element or template, position, rotation...).
    [InlineData("overworld", 12345L, "minecraft:village_plains", -9, -52, 99, "cdf1d66e8f813fb74eac34d2f9393a266c80c921b0627bee2c4efab28acc422e")]
    [InlineData("overworld", 12345L, "minecraft:ancient_city", 32, 37, 89, "159d9e89c3fc0829b4ca415317b18563838e7ae8a9b7635ec51da72106a4f29e")]
    [InlineData("overworld", 12345L, "minecraft:trial_chambers", -95, 103, 215, "3584af2598486ce2031307add7e0ce47c77aa770ae4e4e3ee630160a9e4e8120")]
    [InlineData("nether", 12345L, "minecraft:bastion_remnant", 39, -80, 95, "d070bab00a2e3064d74031c301dfcd291432005c93d8188b76b80d7af90d4cc4")]
    [InlineData("overworld", 0L, "minecraft:mansion", -221, -52, 531, "6846fa96b14dc76e9bbdefa8c8eeec9f5378afdbf27f2ae19cafa4e3f31b1d78")]
    [InlineData("end", 12345L, "minecraft:end_city", 62, 24, 11, "0f2c8912eaabe8f6e7a36a8107f22a279910f15c471904707b654b7248a72782")]
    [InlineData("overworld", 12345L, "minecraft:mineshaft_mesa", -168, 110, 137, "b65e29ac0b98b3f9bb0938e72bec03498dc9f4f53283155299e3e78e0893ae74")]
    [InlineData("overworld", 12345L, "minecraft:stronghold", -105, 124, 115, "9661ea423138c1ed3dd4d8c9f60b84e07424039855a776883939e78be71b0479")]
    public void PiecesMatchVanilla(string dimensionName, long seed, string structure, int chunkX, int chunkZ, int pieceCount, string expected)
    {
        var start = worldgen.Builder(Dimension(dimensionName), seed).Structures!.GetStarts(chunkX, chunkZ)
            .Single(start => start.Structure.Identifier == structure);

        Assert.Equal(pieceCount, start.Pieces.Count);
        Assert.Equal(expected, Sha256(string.Join('\n', start.Pieces.Select(Describe))));
    }

    [Theory]
    // SHA-256 of the block state ids (little-endian uint16) inside each piece box, in piece order and y, z, x order, in full
    // chunks of a vanilla server world.
    [InlineData("overworld", 0L, "minecraft:desert_pyramid", 0, -188, "87df3c1f341909c302fe6d8c73af7cdc6ac2c7923b02cc6ad85643d6389d6438")]
    [InlineData("nether", 12345L, "minecraft:fortress", -86, 39, "2fb0060a343165969d53b12db1b127f1f756a166d0a0308d26a3d03fdbf32401")]
    [InlineData("nether", 12345L, "minecraft:bastion_remnant", 39, -80, "467b5a2995b311e18de9d7aee0106dffd6dfdb448bbd8c37e84d22c644b5a18f")]
    [InlineData("end", 12345L, "minecraft:end_city", 62, 24, "7aeff0397396665040ea4315320d5aa9ae026adb0d355806d6a6baf5130ee432")]
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
                        var id = chunks.Get(x >> 4, z >> 4).GetBlock(x, y, z).GetHashCode();
                        ids.Add((byte)id);
                        ids.Add((byte)(id >> 8));
                    }
                }
            }
        }

        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(ids.ToArray())));
    }

    [Fact]
    public void TreasureMapMatchesVanilla()
    {
        // Vanilla: /loot spawn 100 64 100 loot minecraft:chests/shipwreck_map, then the colors of the map's saved data.
        var builder = worldgen.Builder(MojangDimension.Overworld, 12345L);
        var target = new StructureLocator(builder.Structures!).FindNearest(StructureTags.All["minecraft:on_treasure_maps"], new Vector(100, 64, 100), 50, false);
        Assert.Equal(new Vector(761, 0, 409), target);

        var map = MapData.CreateFresh(target!.Value.X, target.Value.Z, 1, true, true, "minecraft:overworld");
        MapRenderer.RenderBiomePreview(map, (x, z) => builder.GetBiome(x, 0, z));

        Assert.Equal((832, 320), (map.CenterX, map.CenterZ));
        Assert.Equal("98143ccff6247057c83e4d8fa3755e1d9cfc7010a7dcbb4a4daabb2da8d237ff", Convert.ToHexStringLower(SHA256.HashData(map.Colors)));
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
