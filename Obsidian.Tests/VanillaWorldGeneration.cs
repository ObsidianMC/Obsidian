using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using System;
using System.Collections.Generic;
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
        var builder = new ChunkBuilder(seed);
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
        var builder = new ChunkBuilder(seed);
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
        var builder = new ChunkBuilder(seed);
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

    private static string BlockNames(IChunk chunk)
    {
        var blocks = new StringBuilder();
        for (var y = -64; y < 320; y++)
            for (var z = 0; z < 16; z++)
                for (var x = 0; x < 16; x++)
                {
                    var block = chunk.GetBlock(x, y, z);
                    blocks.Append(block.IsAir ? "minecraft:air" : block.UnlocalizedName).Append('\n');
                }

        return blocks.ToString(0, blocks.Length - 1);
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
