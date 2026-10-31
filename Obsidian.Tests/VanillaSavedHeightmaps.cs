using Obsidian.API;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Replays terrain-to-full block changes against vanilla's serialized full-chunk heightmaps.</summary>
[Trait("Category", TestCategory.Worldgen)]
public sealed class VanillaSavedHeightmaps
{
    [Fact]
    public async Task CompletedChunksSaveVanillasDecoratedHeightmaps()
    {
        using var resource = typeof(VanillaSavedHeightmaps).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.saved_heightmaps.json")!;
        using var reference = JsonDocument.Parse(resource);
        var root = reference.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        await FixtureCases.AssertAllAsync(root.GetProperty("cases").EnumerateArray(),
            entry => entry.GetProperty("name").GetString()!, async entry =>
        {
            var pipeline = new WorldgenPipelines().Decoration("minecraft:overworld", root.GetProperty("seed").GetInt64());
            var chunk = ReplayChanges(entry, pipeline);
            await AssertSavedHeightmaps(entry, chunk);
        });
    }

    private static Chunk ReplayChanges(JsonElement entry, BiomeTerrain pipeline)
    {
        var x = entry.GetProperty("chunkX").GetInt32();
        var z = entry.GetProperty("chunkZ").GetInt32();
        var minY = entry.GetProperty("minY").GetInt32();
        var height = entry.GetProperty("height").GetInt32();
        var terrain = VanillaTerrainGeneration.Decode(entry.GetProperty("terrain").GetString()!);
        var full = VanillaTerrainGeneration.Decode(entry.GetProperty("full").GetString()!);
        var chunk = new Chunk(x, z, minY, height);
        Assert.Equal(height * 256, terrain.Length);
        Assert.Equal(terrain.Length, full.Length);

        // Replay the captured terrain, then the changes made by vanilla's remaining generation stages through
        // the same region writer decoration uses. Stored maps must remain stale until the explicit handoff.
        for (var index = 0; index < terrain.Length; index++)
        {
            var y = minY + (index >> 8);
            ((ChunkSection)chunk.Sections[(y - minY) >> 4]).SetBlockUntracked(
                index & 15, y & 15, (index >> 4) & 15, BlocksRegistry.Get(terrain[index]));
        }

        WorldgenHeightmaps.Update(chunk, minY, height);
        WorldgenHeightmaps.UpdateFinal(chunk, minY, height);
        var before = chunk.Heightmaps[HeightmapType.WorldSurface].GetDataArray();
        var chunks = new Dictionary<(int X, int Z), IChunk> { [(x, z)] = chunk };
        var region = new WorldGenRegion(chunks, x, z, 12345, minY, height, 63,
            pipeline.Source, new XoroshiroRandomSource(0), trackHeightmaps: true);

        for (var index = 0; index < full.Length; index++)
        {
            if (full[index] == terrain[index])
                continue;

            region.SetStateId(new Vector((x << 4) + (index & 15), minY + (index >> 8),
                (z << 4) + ((index >> 4) & 15)), full[index]);
        }

        Assert.NotNull(chunk.FinalHeightmaps);
        Assert.Equal(before, chunk.Heightmaps[HeightmapType.WorldSurface].GetDataArray());
        pipeline.CompleteHeightmaps(chunk);
        Assert.Null(chunk.FinalHeightmaps);
        Assert.False(before.SequenceEqual(chunk.Heightmaps[HeightmapType.WorldSurface].GetDataArray()),
            "The capture must exercise decoration above the terrain surface.");
        chunk.SetChunkStatus(ChunkGenStage.full);

        return chunk;
    }

    // Read the actual region-file payload, including the set of saved map types and every packed long.
    private static async Task AssertSavedHeightmaps(JsonElement entry, Chunk chunk)
    {
        var (x, z) = (chunk.X, chunk.Z);
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-saved-heights-{Guid.NewGuid():N}");
        try
        {
            await using (var saving = new Region(x >> 5, z >> 5, folder, "region"))
            {
                await saving.InitAsync();
                saving.SetChunk(chunk);
                await saving.FlushAsync();
            }

            await using var file = new RegionFile(Path.Join(folder, "region", $"r.{x >> 5}.{z >> 5}.mca"),
                NbtCompression.ZLib);
            await file.InitializeAsync();
            var bytes = (await file.GetChunkBytesAsync(x & 31, z & 31))!.Value;
            using var stream = new MemoryStream(bytes.ToArray());
            var saved = Assert.IsType<NbtCompound>(new NbtReader(stream).ReadNextTag());
            Assert.Equal(entry.GetProperty("status").GetString(), saved.GetString("Status"));
            var maps = Assert.IsType<NbtCompound>(saved["Heightmaps"]);
            var expected = entry.GetProperty("heightmaps");
            Assert.Equal(expected.EnumerateObject().Count(), maps.Count);
            foreach (var map in expected.EnumerateObject())
            {
                Assert.Equal(map.Value.EnumerateArray().Select(word => word.GetInt64()),
                    ((NbtArray<long>)maps[map.Name]).GetArray());
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
