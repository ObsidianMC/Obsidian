using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Lighting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for the Mojang (vanilla) overworld generator.
/// Expected values come from running vanilla 1.21.11 world generation with the same seeds.
/// </summary>
public class VanillaWorldGeneration
{
    [Theory]
    [InlineData("12345", 12345L)]
    [InlineData(" -4172144997902289642 ", -4172144997902289642L)]
    [InlineData("Obsidian", 416515707L)] // "Obsidian".hashCode() in Java
    public void ParsesSeedsLikeVanilla(string seed, long expected) => Assert.Equal(expected, RandomState.ParseSeed(seed));

    [Fact]
    public void UnboundRegistryNoiseSamplesAsSeedZero()
    {
        // Registry noises aren't bound to a world; sampling them must bind lazily instead of recursing.
        var unbound = NoiseRegistry.Noises.Temperature;
        var seedZero = new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], 0L).GetOrCreateNoise("minecraft:temperature");

        Assert.Equal(seedZero.GetValue(12.5, 0.0, -40.0), unbound.GetValue(12.5, 0.0, -40.0));
        Assert.Equal(seedZero.MaxValue, unbound.MaxValue);
    }

    [Theory]
    // Vanilla: RandomState.create(overworld, 12345).router().<field>().compute(new SinglePointContext(x, y, z)).
    [InlineData(-1000, 40, -1000, 0.4972870927552263, -0.18318753038877283, 0.29439637809991837, 0.016238079376904718, 64.0)]
    [InlineData(16, 100, -37, -0.34568426279260855, -0.7125518621107382, -0.4050000235438347, -0.4583333333333333, 32.0)]
    [InlineData(2048, 40, 2048, 0.38879813422973103, -0.003034826869616222, 0.1942829890176654, 0.04138873475516909, 56.0)]
    public void NoiseRouterMatchesVanilla(int x, int y, int z, double continents, double erosion, double depth,
        double finalDensity, double preliminarySurfaceLevel)
    {
        var router = new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], 12345L).Router;

        Assert.Equal(continents, router.Continents.GetValue(x, y, z));
        Assert.Equal(erosion, router.Erosion.GetValue(x, y, z));
        Assert.Equal(depth, router.Depth.GetValue(x, y, z));
        Assert.Equal(finalDensity, router.FinalDensity.GetValue(x, y, z));
        Assert.Equal(preliminarySurfaceLevel, router.PreliminarySurfaceLevel.GetValue(x, y, z));
    }

    [Theory]
    // Vanilla: MultiNoiseBiomeSource (overworld preset).getNoiseBiome(quartX, quartY, quartZ, sampler) for seed 12345.
    [InlineData(0, -16, 0, "minecraft:lush_caves")]
    [InlineData(0, 16, 0, "minecraft:ocean")]
    [InlineData(83, 16, -250, "minecraft:plains")]
    public void BiomesMatchVanilla(int quartX, int quartY, int quartZ, string expected)
    {
        var biomeSource = MultiNoiseBiomeSource.Overworld(new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], 12345L));

        Assert.Equal(expected, biomeSource.GetNoiseBiome(quartX, quartY, quartZ).Name);
    }

    [Theory]
    // SHA-256 of the block names (y, z, x order) and quart biomes (y, z, x order) of the chunk after vanilla's
    // biomes, noise and surface steps. Covers aquifers (water, lava), ore veins, bedrock and deepslate gradients.
    [InlineData(0L, -100, 57, "edaa3b78c8f0b63962e8e5850ed595106962f2433fc804af603b0fde54ad5bc4", "b47a5f72bba911f1784155abcc766f2092694f467c708e011fa46074ad02d524")]
    [InlineData(0L, 30, 30, "289d8726ffe03a41f6ef4ec0a75959b7114d5cbd63dcab1b2b9bda8b1487b75c", "63f8f426639d6fc2b74953137db6808987d53a5c94876c949daa5ff2c53126cf")]
    [InlineData(12345L, -100, 57, "c1baf842b304ec003631790b569460e256b68a49905bd7e407ac268d042783db", "9192ce66418d5a40c7fa111038c502d8e5a1ac47201f1a853da58c376168167a")]
    public void ChunkMatchesVanilla(long seed, int chunkX, int chunkZ, string expectedBlocks, string expectedBiomes)
    {
        var builder = new ChunkBuilder(seed, generateStructures: false);
        var chunk = new Chunk(chunkX, chunkZ);

        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);

        var biomes = new StringBuilder();
        for (var quartY = -16; quartY < 80; quartY++)
            for (var quartZ = 0; quartZ < 4; quartZ++)
                for (var quartX = 0; quartX < 4; quartX++)
                    biomes.Append(chunk.GetBiome(quartX << 2, quartY << 2, quartZ << 2).Name).Append('\n');

        Assert.Equal(expectedBlocks, Sha256(BlockNames(chunk)));
        Assert.Equal(expectedBiomes, Sha256(biomes.ToString(0, biomes.Length - 1)));
    }

    [Theory]
    // SHA-256 of the block names (y, z, x order) after vanilla's carvers step. (-5, -5) starts a canyon.
    [InlineData(12345L, -5, -5, "d3bd9e5d5a5ff83427d92cd903f9d813c9cc7b0bb22f75f2e5a3e04c894e5a3b")]
    [InlineData(0L, -7, 12, "600b60ee8b1bb837f681e4d8a9fa817edc3b1e6cdd59ca63acd48608d4e43316")]
    public void CarvedChunkMatchesVanilla(long seed, int chunkX, int chunkZ, string expectedBlocks)
    {
        var builder = new ChunkBuilder(seed, generateStructures: false);
        var chunk = new Chunk(chunkX, chunkZ);

        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);
        builder.ApplyCarvers(chunk);

        Assert.Equal(expectedBlocks, Sha256(BlockNames(chunk)));
    }

    [Theory]
    // SHA-256 of the block names (y, z, x order) after vanilla decorates the chunk, with its neighbors at the carvers step.
    [InlineData(0L, -80, 16, "88c252b5ad3fd77ecf0e65e40aab206b638a5464416276ac66672b2c4eeccaeb")] // swamp
    [InlineData(0L, -88, 72, "74981613635a7a2a56b13d40cc76abccc00bf2efa7543916e0157e2175b5690a")] // dark forest
    [InlineData(12345L, -104, 104, "e5f90f049ab61f9282e771850dfdabeb5f9682e791498296ecc13d9cebea8253")] // warm ocean
    [InlineData(12345L, -24, 24, "cc09f17ef2c40975b36a18115957278bf46d7a4c7d2d94090768b829c6e94f65")] // plains
    [InlineData(12345L, -140, 28, "1c65b8e967b38f9f9eaa03190e123d43aa8e6fa8b39df9ed2e3672bc0cd46939")] // desert with a fossil
    public void DecoratedChunkMatchesVanilla(long seed, int chunkX, int chunkZ, string expectedBlocks)
    {
        var builder = new ChunkBuilder(seed, generateStructures: false);
        var area = new Dictionary<(int X, int Z), IChunk>();

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var neighbor = new Chunk(chunkX + dx, chunkZ + dz);
                builder.PopulateBiomes(neighbor);
                builder.Generate3DTerrain(neighbor);
                builder.ApplySurfaceRules(neighbor);
                builder.ApplyCarvers(neighbor);
                area[(neighbor.X, neighbor.Z)] = neighbor;
            }
        }

        builder.Decorate(area, chunkX, chunkZ);

        Assert.Equal(expectedBlocks, Sha256(BlockNames(area[(chunkX, chunkZ)])));
    }

    [Theory]
    // SHA-256 of the block names (y, z, x order) of a full chunk in a vanilla server world with structures disabled.
    // These chunks don't depend on the order their neighbors are decorated in.
    [InlineData("nether", 12345L, 0, 0, "0d67d2e72363035b58dae710879f79f6d01a6e3142697ac8bd88685081cb38ae")]
    [InlineData("end", 12345L, 2, -2, "2df6a200c97c9d1a52f59344ccfdfceea0f48a5868836d9ce4f56b42f4253038")] // guarded spike
    [InlineData("end", 12345L, 83, 0, "b566d9162bd39f686d2b94814b7327a766a65c62d6415432201b692aebf19dda")] // outer islands
    public void FullChunkMatchesVanilla(string dimensionName, long seed, int chunkX, int chunkZ, string expectedBlocks)
    {
        var dimension = dimensionName == "nether" ? MojangDimension.Nether : MojangDimension.End;
        var chunks = new FullChunks(new ChunkBuilder(dimension, seed, generateStructures: false), dimension);

        Assert.Equal(expectedBlocks, Sha256(BlockNames(chunks.Get(chunkX, chunkZ))));
    }

    [Theory]
    // Vanilla: the spawn in level.dat of a new server world with structures disabled.
    [InlineData(12345L, 96, 136, -32)]
    [InlineData(-4172144997902289642L, -448, 71, -432)]
    public void SpawnMatchesVanilla(long seed, int x, int y, int z)
    {
        var builder = new ChunkBuilder(seed, generateStructures: false);
        var chunks = new FullChunks(builder, MojangDimension.Overworld);
        var (climateX, climateZ) = builder.FindClimateSpawn();

        Vector? spawn = null;
        foreach (var (dx, dz) in SpawnFinder.SpiralOffsets())
        {
            spawn = SpawnFinder.FindSpawnInChunk(chunks.Get((climateX >> 4) + dx, (climateZ >> 4) + dz), hasCeiling: false);
            if (spawn is not null)
                break;
        }

        Assert.Equal(new Vector(x, y, z), spawn);
    }

    [Theory]
    // SHA-256 of the chunk's sky light arrays, then its block light arrays (one per section, bottom up, in vanilla's nibble
    // layout), once vanilla has lit the chunk and its neighbors. The 7x7 chunks around it are carved and the 5x5 around
    // it decorated first, z then x ascending, so the 3x3 around it has its final blocks. Overworld values come from vanilla
    // generating and lighting the same area in that order; nether and end values from a vanilla server world with
    // structures disabled, whose 3x3 around these chunks doesn't depend on the order chunks are decorated in.
    [InlineData("overworld", 0L, 0, 0, "c5a236f2535dabb2bffaabe001eed61884158ae8d2a7be44968c8e9bde0f94d2")] // leaves, water, lava
    [InlineData("overworld", 12345L, 48, -48, "40d33e7ea46e2663acc8bf12c53a0ab5b5720af0f56a1253cf36cf54bfe394fd")] // snow layers
    [InlineData("nether", 12345L, 0, 0, "9e57c3150eb6c9d57c511818120f60b44f8e3c1f82f4b2c85b0cfd9071ce79c1")] // no sky light
    [InlineData("end", 12345L, 83, 0, "84a492595a3a63173ee97654affe5e04dee7e4101e870b2eecda1e421fd09dff")] // islands over the void
    public void LitChunkMatchesVanilla(string dimensionName, long seed, int chunkX, int chunkZ, string expectedLight)
    {
        var dimension = dimensionName switch
        {
            "nether" => MojangDimension.Nether,
            "end" => MojangDimension.End,
            _ => MojangDimension.Overworld
        };
        var builder = new ChunkBuilder(dimension, seed, generateStructures: false);
        var area = new Dictionary<(int X, int Z), IChunk>();

        for (var dz = -3; dz <= 3; dz++)
        {
            for (var dx = -3; dx <= 3; dx++)
            {
                var chunk = new Chunk(chunkX + dx, chunkZ + dz, dimension.MinY, dimension.Height);
                builder.PopulateBiomes(chunk);
                builder.Generate3DTerrain(chunk);
                builder.ApplySurfaceRules(chunk);
                builder.ApplyCarvers(chunk);
                area[(chunk.X, chunk.Z)] = chunk;
            }
        }

        for (var dz = -2; dz <= 2; dz++)
        {
            for (var dx = -2; dx <= 2; dx++)
                builder.Decorate(area, chunkX + dx, chunkZ + dz);
        }

        // The order chunks are lit in doesn't change the result; the center is lit halfway through.
        var lit = new List<IChunk>();
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var chunk = area[(chunkX + dx, chunkZ + dz)];
                LightEngine.LightChunk(chunk, lit.Where(other => Math.Abs(other.X - chunk.X) <= 1 && Math.Abs(other.Z - chunk.Z) <= 1),
                    hasSkyLight: dimension != MojangDimension.Nether);
                lit.Add(chunk);
            }
        }

        var sections = area[(chunkX, chunkZ)].Sections;
        var light = sections.SelectMany(section => section.SkyLightArray.ToArray())
            .Concat(sections.SelectMany(section => section.BlockLightArray.ToArray()))
            .ToArray();

        Assert.Equal(expectedLight, Convert.ToHexStringLower(SHA256.HashData(light)));
    }

    [Theory]
    // SHA-256 of the block names (y, z, x order) inside a structure's piece boxes in a vanilla server world, once placed.
    [InlineData(0L, "minecraft:desert_pyramid", 0, -188, "33e24d9c2ea08d96ddf92390572f70104be38e9973342bfc522d2f5eec990f5b")]
    [InlineData(12345L, "minecraft:swamp_hut", -149, -87, "2371b7af33976e6c2fab5f7c429854db45574b6397a85f618ce6f57b581711df")]
    [InlineData(12345L, "minecraft:buried_treasure", 46, 64, "f972eed34b452e4fc6a485e1ed778a07e2398f79a4ceb60ad865a4d18b4de213")]
    [InlineData(12345L, "minecraft:fortress", -86, 39, "a20f4a5fbbef79bf0ca51f23b3173f7ea59b915a420c5f585319e7552edbc3a1", "nether")]
    [InlineData(12345L, "minecraft:monument", 37, -152, "eeada366bfd75334367e02a3fb1cf81bd1a3b052e0f684e637cac27cd621fa81")]
    public void StructureMatchesVanilla(long seed, string structure, int startX, int startZ, string expectedBlocks, string dimensionName = "overworld")
    {
        var dimension = dimensionName == "nether" ? MojangDimension.Nether : MojangDimension.Overworld;
        var builder = new ChunkBuilder(dimension, seed);
        var chunks = new FullChunks(builder, dimension);
        var start = builder.Structures!.GetStarts(startX, startZ).Single(start => start.Structure.Identifier == structure);

        // Placing the pieces settles them on the ground, so the box is read once every chunk they reach is complete.
        var box = start.BoundingBox;
        var complete = new Dictionary<(int X, int Z), IChunk>();
        for (var x = box.MinX >> 4; x <= box.MaxX >> 4; x++)
        {
            for (var z = box.MinZ >> 4; z <= box.MaxZ >> 4; z++)
                complete[(x, z)] = chunks.Get(x, z);
        }

        box = BlockBox.Encapsulating(start.Pieces.Select(piece => piece.BoundingBox))!.Value;
        var blocks = new StringBuilder();
        for (var y = box.MinY; y <= box.MaxY; y++)
        {
            for (var z = box.MinZ; z <= box.MaxZ; z++)
            {
                for (var x = box.MinX; x <= box.MaxX; x++)
                {
                    // Multi-piece structures leave gaps between their pieces, which hold terrain rather than the structure.
                    if (!start.Pieces.Any(piece => piece.BoundingBox.IsInside(x, y, z)))
                        continue;

                    var block = complete[(x >> 4, z >> 4)].GetBlock(x, y, z);
                    blocks.Append(block.IsAir ? "minecraft:air" : block.UnlocalizedName).Append('\n');
                }
            }
        }

        Assert.Equal(expectedBlocks, Sha256(blocks.ToString(0, blocks.Length - 1)));
    }

    [Theory]
    // SHA-256 of vanilla's pieces for a structure start: one "type box orientation genDepth" line per piece, in order.
    [InlineData(12345L, "minecraft:mineshaft", -3, 10, "5af12ad6be35a55d49e0ab229c60a5b54be27c08be4420e08fcd3d80e7e402d3")]
    [InlineData(12345L, "minecraft:mineshaft_mesa", -168, 110, "a40507154fe61789d0f25a7cfa25fe27da190df8fb06beb0741ebc72d0a9cf1e")]
    [InlineData(12345L, "minecraft:stronghold", -105, 124, "cb6d7bb42f8097fa44e5c50a491b4ed7c0f50d95815b30460f97e94e41e7557a")]
    [InlineData(0L, "minecraft:stronghold", -87, 71, "7e3c3ec73f0a89b4e39d5a3c0d14b1c8b3b39129e5eadc0dccbb977445b9b5b9")]
    public void StructurePiecesMatchVanilla(long seed, string structure, int startX, int startZ, string expectedPieces)
    {
        var builder = new ChunkBuilder(seed);
        var start = builder.Structures!.GetStarts(startX, startZ).Single(start => start.Structure.Identifier == structure);

        var pieces = start.Pieces.Select(piece =>
        {
            var box = piece.BoundingBox;
            var orientation = piece.Orientation?.ToString().ToLowerInvariant() ?? "none";
            return $"{piece.GetType().Name.ToLowerInvariant()} {box.MinX},{box.MinY},{box.MinZ},{box.MaxX},{box.MaxY},{box.MaxZ} {orientation} {piece.GenDepth}";
        });

        Assert.Equal(expectedPieces, Sha256(string.Join('\n', pieces)));
    }

    private static string BlockNames(IChunk chunk)
    {
        var blocks = new StringBuilder();
        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y++)
            for (var z = 0; z < 16; z++)
                for (var x = 0; x < 16; x++)
                {
                    var block = chunk.GetBlock(x, y, z);
                    blocks.Append(block.IsAir ? "minecraft:air" : block.UnlocalizedName).Append('\n');
                }

        return blocks.ToString(0, blocks.Length - 1);
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Generates full chunks on demand like a server: chunks are carved, then decorated once all their neighbors are.
    /// </summary>
    private sealed class FullChunks(ChunkBuilder builder, MojangDimension dimension)
    {
        private readonly Dictionary<(int X, int Z), IChunk> carved = [];
        private readonly HashSet<(int X, int Z)> decorated = [];

        public IChunk Get(int chunkX, int chunkZ)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                    this.Decorate(chunkX + dx, chunkZ + dz);
            }

            var chunk = this.Carve(chunkX, chunkZ);
            builder.UpdateFinalHeightmaps(chunk);
            return chunk;
        }

        private void Decorate(int chunkX, int chunkZ)
        {
            if (!this.decorated.Add((chunkX, chunkZ)))
                return;

            var area = new Dictionary<(int X, int Z), IChunk>();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                    area[(chunkX + dx, chunkZ + dz)] = this.Carve(chunkX + dx, chunkZ + dz);
            }

            builder.Decorate(area, chunkX, chunkZ);
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
