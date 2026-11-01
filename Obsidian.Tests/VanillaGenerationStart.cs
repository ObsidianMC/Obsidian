using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang.Features;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

[Trait("Category", TestCategory.Worldgen)]
public sealed class VanillaGenerationStart
{
    [Fact]
    public void SpawnTargetsMatchVanilla()
    {
        FixtureCases.AssertAll(VanillaWorldgenCaptures.CurrentCases("spawn_search"), Name, entry =>
        {
            var settings = NoiseGeneratorSettings.Read(WorldgenData.Vanilla, entry.GetProperty("settings").GetString()!);
            if (entry.TryGetProperty("targets", out var targets))
            {
                var json = JsonSerializer.SerializeToNode(WorldgenData.Vanilla.Read(
                    "noise_settings", entry.GetProperty("settings").GetString()!))!;
                json["spawn_target"] = System.Text.Json.Nodes.JsonNode.Parse(targets.GetRawText());
                settings = NoiseGeneratorSettings.Read(JsonSerializer.SerializeToElement(json), "custom");
            }

            var compiler = new DensityCompiler(WorldgenData.Vanilla, entry.GetProperty("seed").GetInt64(),
                settings.LegacyRandomSource);
            var position = SpawnSearch.Find(settings.SpawnTargets, compiler);
            Assert.Equal((entry.GetProperty("position")[0].GetInt32(), entry.GetProperty("position")[2].GetInt32()),
                position);
        });
    }

    [Fact]
    public void InitialPopulationMatchesVanilla()
    {
        FixtureCases.AssertAll(VanillaWorldgenCaptures.CurrentCases("population"), Name, entry =>
        {
            var seed = entry.GetProperty("seed").GetInt64();
            var cx = entry.GetProperty("chunkX").GetInt32();
            var cz = entry.GetProperty("chunkZ").GetInt32();
            CodecRegistry.TryGetBiome(entry.GetProperty("biome").GetString()!, out var biome);
            CodecRegistry.TryGetDimension("minecraft:overworld", out var dimension);
            var area = new Dictionary<(int X, int Z), IChunk>();
            var pipeline = BiomeTerrain.Create(WorldgenDimension.Overworld, seed);
            for (var z = cz - 1; z <= cz + 1; z++)
            {
                for (var x = cx - 1; x <= cx + 1; x++)
                {
                    var chunk = new Chunk(x, z);
                    var bottom = chunk.MinY;
                    foreach (var layer in entry.GetProperty("layers").EnumerateArray())
                    {
                        var block = BlocksRegistry.Get(layer[0].GetString()!);
                        var top = layer[1].GetInt32();
                        for (var y = bottom; y <= top; y++)
                        {
                            for (var localZ = 0; localZ < 16; localZ++)
                            {
                                for (var localX = 0; localX < 16; localX++)
                                    chunk.SetBlock(localX, y, localZ, block);
                            }
                        }
                        bottom = top + 1;
                    }

                    for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
                    {
                        for (var localZ = 0; localZ < 16; localZ += 4)
                        {
                            for (var localX = 0; localX < 16; localX += 4)
                                chunk.SetBiome(localX, y, localZ, biome!);
                        }
                    }

                    chunk.SetChunkStatus(ChunkGenStage.features);
                    area[(x, z)] = chunk;
                }
            }

            var compiler = new DensityCompiler(WorldgenData.Vanilla, seed, false);
            var random = GenerationPopulation.Random(seed, cx, cz);
            var regionRandom = compiler.RandomFactory("minecraft:worldgen_region_random").At(cx << 4, 0, cz << 4);
            var region = new WorldGenRegion(area, cx, cz, seed, -64, 384, 63, pipeline.Source,
                regionRandom, trackHeightmaps: true);
            GenerationPopulation.Spawn(region, PluginSpawns.Current.At(dimension!, biome!), cx, cz, false, random);

            var actual = ((Chunk)area[(cx, cz)]).PendingEntities.Select(entity =>
                (entity.Position.X, entity.Position.Y, entity.Position.Z, entity.Yaw, entity.Pitch, entity.Type)).ToArray();
            var expected = entry.GetProperty("entities").EnumerateArray().Select(entity =>
                (entity[0].GetDouble(), entity[1].GetDouble(), entity[2].GetDouble(),
                    entity[3].GetSingle(), entity[4].GetSingle(), entity[5].GetString()!)).ToArray();
            Assert.Equal(expected, actual);
            Assert.Equal(entry.GetProperty("nextRandom").GetInt32(), random.NextInt());
        });
    }

    private static string Name(JsonElement entry) => entry.GetProperty("name").GetString()!;
}
