using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Farmland and the crop planted on it keep through their chunk saving and loading.
/// </summary>
public sealed class Farmland
{
    private const int Ground = 69;

    private static readonly Vector Farm = new(8, Ground, 8);

    [Fact(DisplayName = "Wheat planted on farmland is still there after its chunk unloads and loads again")]
    public async Task PlantedCropSurvivesReload()
    {
        await using var context = TestServerFactory.CreateServer($"farm-reload-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        await world.SetBlockAsync(Farm, BlocksRegistry.GetDefault(BlockIds.Farmland));
        await world.SetBlockAsync(Farm + Vector.Up, BlocksRegistry.GetDefault(BlockIds.Wheat));
        await world.DoWorldTickAsync();
        Assert.True(Held(world).GetBlock(Farm + Vector.Up).Is(BlockIds.Wheat));

        var region = world.GetRegionForChunk(0, 0)!;
        await region.UnloadChunk(0, 0);
        var loaded = (await region.GetChunkAsync(0, 0))!;

        Assert.True(loaded.GetBlock(Farm).Is(BlockIds.Farmland));
        Assert.True(loaded.GetBlock(Farm + Vector.Up).Is(BlockIds.Wheat));
    }

    // The level may replace its chunk objects as blocks change, so read the one it holds each time.
    private static IChunk Held(WorldData.World world) => world.GetLoadedChunk(0, 0)!;

    private static void AddChunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new WorldData.Chunk(chunkX, chunkZ);

                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        chunk.SetBlock(chunkX * 16 + x, Ground, chunkZ * 16 + z, BlocksRegistry.GrassBlock);

                        if (x % 4 != 0 || z % 4 != 0)
                            continue;

                        for (var y = -64; y < 320; y += 4)
                            chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
                    }
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                world.Fluids.Track(chunk);
            }
        }
    }
}
