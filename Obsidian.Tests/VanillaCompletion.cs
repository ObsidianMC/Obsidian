using Obsidian.API;
using Obsidian.API.World;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Fluids;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

[Trait("Category", TestCategory.Worldgen)]
public sealed class VanillaCompletion
{
    [Fact]
    public void FullChunksMatchVanilla()
    {
        FixtureCases.AssertAll(VanillaWorldgenCaptures.CurrentCases("completion"),
            entry => entry.GetProperty("name").GetString()!, Replay);
    }

    private static void Replay(JsonElement entry)
    {
        var dimension = WorldgenDimension.Vanilla(entry.GetProperty("dimension").GetString()!);
        var pipeline = BiomeTerrain.Create(dimension, entry.GetProperty("seed").GetInt64(),
            new FeatureRegistry(WorldgenData.Vanilla));
        var x = entry.GetProperty("chunkX").GetInt32();
        var z = entry.GetProperty("chunkZ").GetInt32();
        var area = new Dictionary<(int X, int Z), IChunk>();
        // Replay completion from vanilla's actual pre-FULL inputs. Neighbor feature ordering is a separate
        // replay: these blocks, marks and tick lists were captured before LevelChunk.postProcessGeneration.
        foreach (var input in entry.GetProperty("inputs").EnumerateArray())
        {
            var cx = input.GetProperty("chunkX").GetInt32();
            var cz = input.GetProperty("chunkZ").GetInt32();
            var chunk = new Chunk(cx, cz, pipeline.MinY, pipeline.Height);
            VanillaTerrainGeneration.RestoreBlocks(input.GetProperty("blocks"), chunk);
            chunk.SetChunkStatus(ChunkGenStage.features);
            foreach (var mark in input.GetProperty("marks").EnumerateArray())
                chunk.PostProcessing.Add(Position(mark));
            foreach (var tick in input.GetProperty("blockTicks").EnumerateArray())
                chunk.BlockTicks.Schedule(Position(tick), tick[3].GetString()!, tick[4].GetInt32(), tick[5].GetInt32());
            foreach (var tick in input.GetProperty("fluidTicks").EnumerateArray())
            {
                var kind = FluidNames.Parse(tick[3].GetString());
                chunk.FluidTicks.Schedule(Position(tick), kind, tick[4].GetInt32(), tick[5].GetInt32());
            }
            area[(cx, cz)] = chunk;
        }

        AssertMushroomLight("mushroomLightBefore");
        pipeline.PostProcess(area, x, z);
        AssertMushroomLight("mushroomLightAfter");
        var center = (Chunk)area[(x, z)];
        pipeline.CompleteHeightmaps(center);
        VanillaTerrainGeneration.AssertBlocks(entry.GetProperty("blocks"), center);

        var blocks = center.BlockTicks.Pack().Select(tick =>
            Row(tick.Position, tick.Block, tick.Delay, tick.Priority)).Order().ToArray();
        var fluids = center.FluidTicks.Pack().Select(tick =>
            Row(tick.Position, tick.Type.Name(), tick.Delay, tick.Priority)).Order().ToArray();
        Assert.Equal(Ticks(entry, "blockTicks"), blocks);
        Assert.Equal(Ticks(entry, "fluidTicks"), fluids);

        void AssertMushroomLight(string field)
        {
            var light = new CompletionLight(area, dimension.DimensionType.Element.HasSkylight);
            foreach (var sample in entry.GetProperty(field).EnumerateArray())
            {
                var position = Position(sample);
                var stored = sample[3].GetInt32();
                // CompletionLight only resolves survival's light-13 boundary, not the complete light value.
                Assert.True((stored >= 13) == (light.MushroomBrightness(position) >= 13),
                    $"{field} at {position}: vanilla stored {stored}, completion threshold differs");
            }
        }
    }

    private static string[] Ticks(JsonElement entry, string field) => entry.GetProperty(field).EnumerateArray()
        .Select(tick => Row(new Vector(tick[0].GetInt32(), tick[1].GetInt32(), tick[2].GetInt32()),
            tick[3].GetString()!, tick[4].GetInt32(), tick[5].GetInt32())).Order().ToArray();

    private static Vector Position(JsonElement row) =>
        new(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32());

    private static string Row(Vector position, string type, int delay, int priority) =>
        $"{position.X},{position.Y},{position.Z} {type} {delay} {priority}";
}
