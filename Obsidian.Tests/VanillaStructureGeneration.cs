using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
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
/// Parity checks for the template and jigsaw structures. Expected values come from running vanilla 1.21.11 with the same
/// seeds: <c>Structure.generate</c> piece lists, and blocks of real vanilla server worlds.
/// </summary>
public class VanillaStructureGeneration
{
    [Theory]
    // SHA-256 of the piece list (one line per piece: type, box, pool element or template, position, rotation...).
    [InlineData("overworld", 12345L, "minecraft:village_plains", -9, -52, 99, "cdf1d66e8f813fb74eac34d2f9393a266c80c921b0627bee2c4efab28acc422e")]
    [InlineData("overworld", 12345L, "minecraft:village_snowy", 91, 75, 67, "a861ac454d0d522ce5a258f9385e916a2c27969047fe34e97611dda6f2e0b38e")]
    [InlineData("overworld", 12345L, "minecraft:pillager_outpost", 17, -60, 11, "c566483c0920f0ab2fd9749e8bbdb76a59e6d007a13e057c9d77b8e6219d9763")]
    [InlineData("overworld", 12345L, "minecraft:ancient_city", 32, 37, 89, "159d9e89c3fc0829b4ca415317b18563838e7ae8a9b7635ec51da72106a4f29e")]
    [InlineData("overworld", 12345L, "minecraft:trial_chambers", -95, 103, 215, "3584af2598486ce2031307add7e0ce47c77aa770ae4e4e3ee630160a9e4e8120")]
    [InlineData("overworld", 12345L, "minecraft:trail_ruins", -113, 43, 18, "628f6607e3232b8537e2daef7d5994f0a0339f4c52ff5015f24be06446b120b2")]
    [InlineData("nether", 12345L, "minecraft:bastion_remnant", 39, -80, 95, "d070bab00a2e3064d74031c301dfcd291432005c93d8188b76b80d7af90d4cc4")]
    [InlineData("overworld", 0L, "minecraft:mansion", -221, -52, 531, "6846fa96b14dc76e9bbdefa8c8eeec9f5378afdbf27f2ae19cafa4e3f31b1d78")]
    [InlineData("end", 12345L, "minecraft:end_city", 62, 24, 11, "0f2c8912eaabe8f6e7a36a8107f22a279910f15c471904707b654b7248a72782")]
    [InlineData("overworld", 12345L, "minecraft:igloo", -10, 83, 1, "5ab39e4561841d3004a5ac4899c2c9b84545a5556ad8ac5d38f62401cff97720")]
    [InlineData("overworld", 12345L, "minecraft:ruined_portal_mountain", 83, 89, 1, "71a8a27c30fdc2c99599e99e00b53c11addf133de73dad6206dd0d26d2ab69a5")]
    [InlineData("overworld", 12345L, "minecraft:ocean_ruin_cold", -16, 5, 3, "5487db588e1e52474d3e3e252f23eb63f3651812ddbca3a59d97c4fa28e4c4d3")]
    [InlineData("overworld", 12345L, "minecraft:shipwreck", 53, 82, 1, "db6a50832d16218b67867e9f3e8f9f2c9427527a0db20d93dfb7a42108b52a48")]
    [InlineData("nether", 12345L, "minecraft:nether_fossil", -64, 40, 1, "ee33daf20d923c5235c34c67fe257ec9787c5b554afd177331c121353ed6f761")]
    public void PiecesMatchVanilla(string dimensionName, long seed, string structure, int chunkX, int chunkZ, int pieceCount, string expected)
    {
        var start = new ChunkBuilder(Dimension(dimensionName), seed).Structures!.GetStarts(chunkX, chunkZ).Single(start => start.Structure.Identifier == structure);

        Assert.Equal(pieceCount, start.Pieces.Count);
        Assert.Equal(expected, Sha256(string.Join("\n", start.Pieces.Select(Describe))));
    }

    [Theory]
    // SHA-256 of the block state ids (little-endian uint16) inside each piece box, in piece order and y, z, x order, in full
    // chunks of a vanilla server world.
    [InlineData("end", 12345L, "minecraft:end_city", 62, 24, "7aeff0397396665040ea4315320d5aa9ae026adb0d355806d6a6baf5130ee432")]
    [InlineData("overworld", 12345L, "minecraft:igloo", -10, 83, "738c079dff6c9b77a0891ac42db1cabcab933a672b14aed8ecfcf94c0e77bb40")]
    [InlineData("overworld", 12345L, "minecraft:ruined_portal_mountain", 83, 89, "d9c32e1667bd9ab536fc5975c8b9fee9485f777a5937f3bba070e0c376e34214")]
    [InlineData("nether", 12345L, "minecraft:bastion_remnant", 39, -80, "467b5a2995b311e18de9d7aee0106dffd6dfdb448bbd8c37e84d22c644b5a18f")]
    public void BlocksMatchVanilla(string dimensionName, long seed, string structure, int chunkX, int chunkZ, string expected)
    {
        var dimension = Dimension(dimensionName);
        var builder = new ChunkBuilder(dimension, seed);
        var chunks = new FullChunks(builder, dimension);
        var start = builder.Structures!.GetStarts(chunkX, chunkZ).Single(start => start.Structure.Identifier == structure);
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

    /// <summary>
    /// Chunks generated like vanilla full chunks: decorated along with their neighbors, then post-processed.
    /// </summary>
    private sealed class FullChunks(ChunkBuilder builder, MojangDimension dimension)
    {
        private readonly Dictionary<(int X, int Z), IChunk> carved = [];
        private readonly HashSet<(int X, int Z)> decorated = [];
        private readonly HashSet<(int X, int Z)> finished = [];

        public IChunk Get(int chunkX, int chunkZ)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                    this.Decorate(chunkX + dx, chunkZ + dz);
            }

            var chunk = this.Carve(chunkX, chunkZ);
            if (this.finished.Add((chunkX, chunkZ)))
            {
                builder.PostProcess(this.Area(chunkX, chunkZ), chunkX, chunkZ);
                builder.UpdateFinalHeightmaps(chunk);
            }

            return chunk;
        }

        private void Decorate(int chunkX, int chunkZ)
        {
            if (this.decorated.Add((chunkX, chunkZ)))
                builder.Decorate(this.Area(chunkX, chunkZ), chunkX, chunkZ);
        }

        private Dictionary<(int X, int Z), IChunk> Area(int chunkX, int chunkZ)
        {
            var area = new Dictionary<(int X, int Z), IChunk>();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                    area[(chunkX + dx, chunkZ + dz)] = this.Carve(chunkX + dx, chunkZ + dz);
            }

            return area;
        }

        private IChunk Carve(int chunkX, int chunkZ)
        {
            if (this.carved.TryGetValue((chunkX, chunkZ), out var chunk))
                return chunk;

            chunk = new Chunk(chunkX, chunkZ, dimension.MinY, dimension.Height);
            builder.PopulateBiomes(chunk);
            builder.Generate3DTerrain(chunk);
            builder.ApplySurfaceRules(chunk);
            builder.ApplyCarvers(chunk);
            this.carved[(chunkX, chunkZ)] = chunk;
            return chunk;
        }
    }
}
